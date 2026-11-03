namespace CookMate.Api.Entities;

// Giá trị lưu trong cột Status (CHECK 0..2 trong DB)
public static class RecipeStatus
{
    public const int Pending = 0;
    public const int Approved = 1;
    public const int Rejected = 2;
}

public class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }

    // 1 = dễ, 2 = trung bình, 3 = khó (ràng buộc CHECK trong DB)
    public int Difficulty { get; set; }
    public int PrepMinutes { get; set; }
    public int CookMinutes { get; set; }
    public int BaseServings { get; set; }

    // Nhãn khẩu vị, cách nhau bằng dấu phẩy (xem TasteTags)
    public string Tastes { get; set; } = "";

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // null = công thức hệ thống (seed), có giá trị = người dùng chia sẻ
    public int? AuthorId { get; set; }
    public User? Author { get; set; }

    public int Status { get; set; } = RecipeStatus.Approved;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }
    public string? RejectReason { get; set; }

    // Xóa mềm: CookingSession vẫn tham chiếu được tới bài đã xóa
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public List<RecipeStep> Steps { get; set; } = [];
}
