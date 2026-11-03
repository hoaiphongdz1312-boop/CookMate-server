using System.Security.Cryptography;
using System.Text;
using CookMate.Api.Data;
using CookMate.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CookMate.Api.Services;

public enum CodeCheck
{
    Ok,
    Invalid,
    Expired
}

// Tạo, gửi và kiểm tra mã 6 số gửi về email.
public class EmailCodeService(AppDbContext db, IEmailSender sender, JwtOptions jwt)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    /// <summary>
    /// Tạo mã mới (hủy mã cũ cùng mục đích) rồi gửi email.
    /// Trả false nếu gửi lại quá nhanh (chưa đủ 60 giây).
    /// </summary>
    public async Task<bool> IssueAsync(User user, EmailCodePurpose purpose, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var old = await db.EmailCodes
            .Where(c => c.UserId == user.Id && c.Purpose == purpose)
            .ToListAsync(ct);

        if (old.Any(c => now - c.CreatedAt < ResendCooldown))
            return false;

        db.EmailCodes.RemoveRange(old);

        // Mã xác thực email chỉ sống bằng thời gian tài khoản chưa xác thực được giữ lại
        var lifetime = purpose == EmailCodePurpose.VerifyEmail ? UnverifiedUserCleanup.MaxAge : Lifetime;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.EmailCodes.Add(new EmailCode
        {
            UserId = user.Id,
            Purpose = purpose,
            CodeHash = Hash(user.Id, purpose, code),
            CreatedAt = now,
            ExpiresAt = now + lifetime
        });
        await db.SaveChangesAsync(ct);

        var (subject, body) = purpose == EmailCodePurpose.VerifyEmail
            ? ("CookMate: mã xác thực email",
               $"Xin chào {user.FullName},\n\nMã xác thực email của bạn là: {code}\nMã có hiệu lực {lifetime.TotalMinutes:0} phút. Nếu bạn không đăng ký CookMate, hãy bỏ qua email này.")
            : ("CookMate: mã đặt lại mật khẩu",
               $"Xin chào {user.FullName},\n\nMã đặt lại mật khẩu của bạn là: {code}\nMã có hiệu lực {lifetime.TotalMinutes:0} phút. Nếu bạn không yêu cầu, hãy bỏ qua email này và mật khẩu vẫn giữ nguyên.");

        await sender.SendAsync(user.Email, subject, body, ct);
        return true;
    }

    /// <summary>Kiểm tra mã. Đúng thì xóa mã (dùng một lần); sai thì tăng số lần thử.</summary>
    public async Task<CodeCheck> VerifyAsync(User user, EmailCodePurpose purpose, string code, CancellationToken ct = default)
    {
        var entry = await db.EmailCodes
            .SingleOrDefaultAsync(c => c.UserId == user.Id && c.Purpose == purpose, ct);
        if (entry == null)
            return CodeCheck.Invalid;

        if (entry.ExpiresAt < DateTime.UtcNow || entry.Attempts >= MaxAttempts)
        {
            db.EmailCodes.Remove(entry);
            await db.SaveChangesAsync(ct);
            return CodeCheck.Expired;
        }

        var expected = Encoding.UTF8.GetBytes(entry.CodeHash);
        var actual = Encoding.UTF8.GetBytes(Hash(user.Id, purpose, code.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            entry.Attempts++;
            await db.SaveChangesAsync(ct);
            return CodeCheck.Invalid;
        }

        db.EmailCodes.Remove(entry);
        await db.SaveChangesAsync(ct);
        return CodeCheck.Ok;
    }

    // Mã chỉ có 1 triệu khả năng nên phải băm kèm khóa bí mật của server (HMAC)
    private string Hash(int userId, EmailCodePurpose purpose, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwt.Key));
        var data = Encoding.UTF8.GetBytes($"{userId}|{purpose}|{code}");
        return Convert.ToHexString(hmac.ComputeHash(data));
    }
}
