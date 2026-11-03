using CookMate.Api.Data;
using CookMate.Api.Dtos;
using CookMate.Api.Entities;
using CookMate.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    JwtTokenService jwt,
    EmailCodeService codes,
    IGoogleTokenVerifier googleVerifier,
    GoogleOptions googleOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string InvalidLogin = "Email hoặc mật khẩu không đúng";
    private const string GoogleFailed = "Đăng nhập Google thất bại";
    private const string InvalidCode = "Mã không đúng hoặc đã hết hạn";
    private const string MailFailed = "Không gửi được email lúc này, vui lòng thử lại sau";

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = NormalizeEmail(req.Email);
        var fullName = req.FullName.Trim();
        if (fullName.Length == 0)
            return BadRequest(new ErrorResponse("Thiếu họ tên"));

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is { EmailConfirmed: true })
            return Conflict(new ErrorResponse("Email đã được đăng ký"));

        if (user == null)
        {
            user = new User { Email = email, CreatedAt = DateTime.UtcNow, EmailConfirmed = false };
            db.Users.Add(user);
        }

        // Email đã đăng ký nhưng chưa xác thực: cho đăng ký lại (ghi đè tên, mật khẩu) rồi gửi mã mới.
        // An toàn vì tài khoản chưa xác thực chưa dùng được.
        // Tính lại hạn xác thực từ lần đăng ký này, kẻo bị UnverifiedUserCleanup xóa ngay
        user.CreatedAt = DateTime.UtcNow;
        user.FullName = fullName;
        user.PasswordHash = hasher.HashPassword(user, req.Password);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Hai request đăng ký cùng email chạy song song: chỉ mục unique chặn lại
            return Conflict(new ErrorResponse("Email đã được đăng ký"));
        }

        if (!await TrySendAsync(user, EmailCodePurpose.VerifyEmail))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse(MailFailed));

        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(user.Email, true));
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest req)
    {
        var user = await FindByEmailAsync(req.Email);
        if (user == null)
            return BadRequest(new ErrorResponse(InvalidCode));

        if (await codes.VerifyAsync(user, EmailCodePurpose.VerifyEmail, req.Code) != CodeCheck.Ok)
            return BadRequest(new ErrorResponse(InvalidCode));

        user.EmailConfirmed = true;
        await db.SaveChangesAsync();
        return Ok(new AuthResponse(jwt.CreateToken(user), user.ToDto()));
    }

    // Luôn trả 204 dù email có tồn tại hay không, để không lộ ai đã đăng ký
    [HttpPost("resend-code")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResendCode(EmailOnlyRequest req)
    {
        var user = await FindByEmailAsync(req.Email);
        if (user is { EmailConfirmed: false } && !await TrySendAsync(user, EmailCodePurpose.VerifyEmail))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse(MailFailed));

        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword(EmailOnlyRequest req)
    {
        var user = await FindByEmailAsync(req.Email);
        if (user != null && !await TrySendAsync(user, EmailCodePurpose.ResetPassword))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse(MailFailed));

        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var user = await FindByEmailAsync(req.Email);
        if (user == null)
            return BadRequest(new ErrorResponse(InvalidCode));

        if (await codes.VerifyAsync(user, EmailCodePurpose.ResetPassword, req.Code) != CodeCheck.Ok)
            return BadRequest(new ErrorResponse(InvalidCode));

        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        // Nhận được mã tại hộp thư nghĩa là đã chứng minh sở hữu email
        user.EmailConfirmed = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await FindByEmailAsync(req.Email);
        // Tài khoản chỉ đăng nhập bằng Google có PasswordHash rỗng: không có mật khẩu để so
        if (user == null || user.PasswordHash.Length == 0)
            return Unauthorized(new ErrorResponse(InvalidLogin));

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new ErrorResponse(InvalidLogin));

        // Chỉ báo "chưa xác thực" sau khi mật khẩu đúng, để không lộ email nào đã đăng ký
        if (!user.EmailConfirmed)
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResponse("Email chưa được xác thực. Vui lòng nhập mã đã gửi về email."));

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, req.Password);
            await db.SaveChangesAsync();
        }

        return Ok(new AuthResponse(jwt.CreateToken(user), user.ToDto()));
    }

    [HttpPost("google")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GoogleLogin(GoogleLoginRequest req)
    {
        if (!googleOptions.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("Máy chủ chưa cấu hình đăng nhập Google"));

        var identity = await googleVerifier.VerifyAsync(req.IdToken, HttpContext.RequestAborted);
        if (identity == null || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email))
            return Unauthorized(new ErrorResponse(GoogleFailed));

        if (!identity.EmailVerified)
            return StatusCode(StatusCodes.Status403Forbidden,
                new ErrorResponse("Email Google chưa được xác thực"));

        var email = NormalizeEmail(identity.Email);
        var user = await FindGoogleUserAsync(identity.Subject, email);
        if (user == null)
        {
            user = new User
            {
                Email = email,
                GoogleId = identity.Subject,
                PasswordHash = "",
                EmailConfirmed = true,
                FullName = GoogleFullName(identity.Name, email),
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
        }
        else if (user.GoogleId == null)
        {
            user.GoogleId = identity.Subject;
            if (!user.EmailConfirmed)
            {
                // Register cho đăng ký lại email chưa xác thực và ghi đè mật khẩu, nên mật khẩu này
                // có thể do kẻ khác đặt. Xóa đi để họ không đăng nhập được vào tài khoản đã gắn Google.
                user.PasswordHash = "";
                user.EmailConfirmed = true;
            }
        }
        else if (user.GoogleId != identity.Subject)
        {
            // Email này đã gắn với một tài khoản Google khác
            return Unauthorized(new ErrorResponse(GoogleFailed));
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Hai request song song cùng tài khoản: chỉ mục unique chặn lại, đọc lại bản đã được tạo
            db.ChangeTracker.Clear();
            user = await FindGoogleUserAsync(identity.Subject, email);
            if (user?.GoogleId != identity.Subject)
                return Unauthorized(new ErrorResponse(GoogleFailed));
        }

        return Ok(new AuthResponse(jwt.CreateToken(user), user.ToDto()));
    }

    private async Task<User?> FindGoogleUserAsync(string googleId, string email) =>
        await db.Users.SingleOrDefaultAsync(u => u.GoogleId == googleId)
        ?? await db.Users.SingleOrDefaultAsync(u => u.Email == email);

    private static string GoogleFullName(string? name, string email)
    {
        var full = string.IsNullOrWhiteSpace(name) ? email[..email.IndexOf('@')] : name.Trim();
        return full.Length > 100 ? full[..100] : full;
    }

    private Task<User?> FindByEmailAsync(string email)
    {
        var normalized = NormalizeEmail(email);
        return db.Users.SingleOrDefaultAsync(u => u.Email == normalized);
    }

    // Không để lỗi SMTP làm sập request; không ghi mã hay mật khẩu vào log
    private async Task<bool> TrySendAsync(User user, EmailCodePurpose purpose)
    {
        try
        {
            // false = gửi lại quá nhanh: coi như đã gửi rồi (mã cũ vẫn còn hiệu lực)
            await codes.IssueAsync(user, purpose);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gửi email {Purpose} thất bại cho user {UserId}", purpose, user.Id);
            return false;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
