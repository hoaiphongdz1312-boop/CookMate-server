using System.Threading.RateLimiting;
using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Connection string lấy từ cấu hình (appsettings.Development.json hoặc user-secrets), không để trong Git
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Thiếu ConnectionStrings:Default. Copy appsettings.Development.example.json thành appsettings.Development.json rồi sửa lại.");
}

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(connectionString));

// JWT: khóa ký đọc từ user-secrets (Jwt:Key), các thông số còn lại ở appsettings.json
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
// HS256 cần khóa tối thiểu 256 bit
if (System.Text.Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
{
    throw new InvalidOperationException(
        "Thiếu Jwt:Key (tối thiểu 32 ký tự). Chạy: dotnet user-secrets set \"Jwt:Key\" \"<chuỗi ngẫu nhiên>\"");
}
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Ảnh bài chia sẻ lưu trên chính máy chạy server (mặc định wwwroot/uploads)
var uploadOptions = new UploadOptions(Path.GetFullPath(
    builder.Configuration["Uploads:Directory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads")));
builder.Services.AddSingleton(uploadOptions);

// Ngưỡng tự duyệt bài cộng đồng (Community:AutoApproveAfter, mặc định 3 bài đã duyệt)
builder.Services.AddSingleton(new CommunityOptions(builder.Configuration.GetValue(
    "Community:AutoApproveAfter", CommunityOptions.DefaultAutoApproveAfter)));

// Ngưỡng nhận diện khuôn mặt: mục "Face" trong appsettings
builder.Services.AddSingleton(builder.Configuration.GetSection("Face").Get<FaceOptions>() ?? new FaceOptions());
builder.Services.AddSingleton<FaceMatchingService>();

// Gửi email: User/Password của SMTP đọc từ user-secrets (Email:User, Email:Password).
// Chưa cấu hình thì ở Development in nội dung email ra log; môi trường khác phải cấu hình.
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
builder.Services.AddSingleton(emailOptions);
if (emailOptions.IsConfigured)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
else
    throw new InvalidOperationException("Thiếu cấu hình Email:User và Email:Password.");
// Đăng nhập Google: ClientId là Web client ID. Thiếu thì ở Development chỉ cảnh báo, endpoint trả 503.
var googleOptions = builder.Configuration.GetSection("Google").Get<GoogleOptions>() ?? new GoogleOptions();
builder.Services.AddSingleton(googleOptions);
builder.Services.AddSingleton<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddScoped<EmailCodeService>();
builder.Services.AddHostedService<UnverifiedUserCleanup>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        // Giữ nguyên tên claim "sub", "email" thay vì đổi sang URI dài của .NET
        opt.MapInboundClaims = false;
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtOptions.SigningKey(),
            // MapInboundClaims tắt nên claim vai trò giữ tên "role"
            RoleClaimType = "role",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        opt.Events = new JwtBearerEvents
        {
            // Trả 401 đúng dạng { "error": ... }
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Chưa đăng nhập hoặc phiên đã hết hạn"));
            },
            // Đã đăng nhập nhưng không đủ quyền (vd. user thường gọi /api/admin/*)
            OnForbidden = async ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Bạn không có quyền thực hiện thao tác này"));
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(opt =>
{
    opt.AddPolicy(RateLimitPolicies.Login, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Giới hạn theo người dùng (claim sub), không theo IP
    opt.AddPolicy(RateLimitPolicies.SharePost, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:SharePerHour", 5),
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));
    opt.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        var message = ctx.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName
            == RateLimitPolicies.SharePost
            ? "Bạn chia sẻ quá nhiều bài, vui lòng thử lại sau"
            : "Thử quá nhiều lần, vui lòng đợi 1 phút";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(message), ct);
    };
});

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(opt =>
    {
        // Lỗi validate trả { "error": "<lỗi đầu tiên>" } thay cho ProblemDetails mặc định
        opt.InvalidModelStateResponseFactory = ctx =>
        {
            var message = ctx.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Dữ liệu không hợp lệ" : e.ErrorMessage)
                .FirstOrDefault() ?? "Dữ liệu không hợp lệ";
            return new BadRequestObjectResult(new ErrorResponse(message));
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo { Title = "CookMate API", Version = "v1" });
    opt.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Dán token nhận được từ /api/auth/login (không cần chữ Bearer)"
    });
    opt.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", doc)] = []
    });
});

// CORS mở hoàn toàn, chỉ dùng cho dev/demo trong mạng LAN
const string DevCors = "DevCors";
builder.Services.AddCors(opt =>
    opt.AddPolicy(DevCors, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (!googleOptions.IsConfigured)
{
    if (app.Environment.IsDevelopment())
        app.Logger.LogWarning("Thiếu Google:ClientId, /api/auth/google sẽ trả 503");
    else
        throw new InvalidOperationException("Thiếu cấu hình Google:ClientId.");
}

// Bắt mọi exception chưa xử lý: ghi log ở server, trả JSON chung, không lộ stack trace
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(ex, "Lỗi chưa xử lý tại {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Lỗi máy chủ, vui lòng thử lại sau"));
}));

// Swagger bật ở Development, hoặc khi cấu hình Swagger:Enabled=true (dùng để demo trên host)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCors);
}

// Ảnh công thức người dùng tải lên, phục vụ ở /uploads (chỉ ảnh, không thực thi gì trong thư mục này)
Directory.CreateDirectory(uploadOptions.Directory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadOptions.Directory),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});

// Ảnh minh họa công thức hệ thống (đi kèm mã nguồn, nguồn và giấy phép ghi trong docs/ANH_MINH_HOA.md)
var seedImagesDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "images");
if (Directory.Exists(seedImagesDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(seedImagesDir),
        RequestPath = "/images",
        OnPrepareResponse = ctx => ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
    });
}

app.UseAuthentication();
// Rate limiter đặt sau Authentication để policy đọc được claim sub
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Seed dữ liệu khi DB còn trống (B2). Thư mục mặc định: server/Data/Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seedDir = Path.GetFullPath(Path.Combine(
        app.Environment.ContentRootPath,
        app.Configuration["Seed:Directory"] ?? "../Data/Seed"));
    await DbSeeder.SeedAsync(db, seedDir, app.Logger);
    await DbSeeder.EnsureAdminAsync(
        db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        app.Configuration["Admin:Email"],
        app.Configuration["Admin:Password"],
        app.Logger);
}

app.Run();

// Cho WebApplicationFactory trong CookMate.Api.Tests
public partial class Program;
