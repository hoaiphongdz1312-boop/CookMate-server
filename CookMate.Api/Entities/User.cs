namespace CookMate.Api.Entities;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";

    // "sub" của tài khoản Google, null nếu chưa liên kết
    public string? GoogleId { get; set; }

    // false cho tới khi người dùng nhập đúng mã gửi về email
    public bool EmailConfirmed { get; set; }

    // Vector khuôn mặt float32 đã chuẩn hóa L2, null nếu chưa đăng ký mặt
    public byte[]? FaceEmbedding { get; set; }
    public int? FaceDim { get; set; }
    public DateTime? FaceEnrolledAt { get; set; }

    // "User" hoặc "Admin"
    public string Role { get; set; } = Roles.User;

    // Nhãn khẩu vị đã chọn (xem TasteTags). null = chưa được hỏi, rỗng = đã bỏ qua
    public string? Tastes { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<CookingSession> CookingSessions { get; set; } = [];
}
