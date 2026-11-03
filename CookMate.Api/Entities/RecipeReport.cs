namespace CookMate.Api.Entities;

// Lý do báo cáo, lưu dạng chuỗi trong cột Reason
public static class ReportReasons
{
    public const string WrongInfo = "wrong_info";
    public const string Spam = "spam";
    public const string Offensive = "offensive";
    public const string Copyright = "copyright";
    public const string Other = "other";

    public static readonly string[] All = [WrongInfo, Spam, Offensive, Copyright, Other];
}

// Giá trị cột Status của báo cáo
public static class ReportStatus
{
    public const int Open = 0;
    public const int Resolved = 1;
}

// Kết quả xử lý báo cáo
public static class ReportResolutions
{
    public const string Removed = "removed";
    public const string Dismissed = "dismissed";
}

// Báo cáo vi phạm một công thức. Mỗi người chỉ báo cáo một bài một lần (index duy nhất RecipeId + ReporterId).
public class RecipeReport
{
    public int Id { get; set; }

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    public string Reason { get; set; } = "";
    // Mô tả thêm của người báo cáo, bắt buộc khi Reason = other
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }

    public int Status { get; set; } = ReportStatus.Open;
    public string? Resolution { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedById { get; set; }
    public User? ResolvedBy { get; set; }
}
