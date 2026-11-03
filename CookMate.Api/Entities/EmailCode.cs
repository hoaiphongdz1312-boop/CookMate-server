namespace CookMate.Api.Entities;

public enum EmailCodePurpose
{
    VerifyEmail,
    ResetPassword
}

// Mã 6 số gửi về email. Chỉ lưu băm của mã, không lưu mã gốc.
public class EmailCode
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public EmailCodePurpose Purpose { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    // Số lần nhập sai, quá giới hạn thì mã bị hủy
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
}
