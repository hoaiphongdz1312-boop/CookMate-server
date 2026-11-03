using CookMate.Api.Data;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CookMate.Api.Tests;

// Chạy cả API thật trong bộ nhớ, thay SQL Server bằng EF InMemory và email bằng bản ghi lại nội dung.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "matkhau-test-1";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly int _loginPerMinute;

    public ApiFactory() : this(1000) { }
    protected ApiFactory(int loginPerMinute) => _loginPerMinute = loginPerMinute;

    public FakeEmailSender Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", "Server=unused;Database=unused");
        builder.UseSetting("Jwt:Key", "khoa-ky-jwt-chi-dung-cho-test-0123456789");
        builder.UseSetting("RateLimit:LoginPerMinute", _loginPerMinute.ToString());
        builder.UseSetting("Uploads:Directory", Path.Combine(Path.GetTempPath(), "cookmate-test-uploads", _dbName));
        builder.UseSetting("Seed:Directory", Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Data", "Seed")));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            // EF 9+ còn đăng ký thêm IDbContextOptionsConfiguration<AppDbContext> (kiểu internal), gỡ theo tên
            foreach (var d in services.Where(d => d.ServiceType.IsGenericType
                         && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                         && d.ServiceType.GenericTypeArguments[0] == typeof(AppDbContext)).ToList())
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    public async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Tạo thẳng tài khoản đã xác thực trong DB (không đi qua API để khỏi tốn lượt rate limit)
    public async Task<User> CreateUserAsync(string email, string fullName = "Người Test", string role = Roles.User)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            Email = email, FullName = fullName, EmailConfirmed = true, Role = role, CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public HttpClient ClientFor(User user)
    {
        var token = Services.GetRequiredService<JwtTokenService>().CreateToken(user);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add((toEmail, subject, body));
        return Task.CompletedTask;
    }
}
