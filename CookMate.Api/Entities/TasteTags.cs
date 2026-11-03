namespace CookMate.Api.Entities;

// Bộ nhãn khẩu vị cố định; Recipe.Tastes lưu các nhãn cách nhau bằng dấu phẩy
public static class TasteTags
{
    public static readonly string[] All = ["cay", "chua", "ngọt", "đậm đà", "thanh nhẹ", "béo"];

    public static List<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Bỏ nhãn lạ và trùng, giữ thứ tự chuẩn của All
    public static string Normalize(IEnumerable<string>? tags) =>
        string.Join(',', All.Where(t => tags?.Contains(t, StringComparer.OrdinalIgnoreCase) == true));
}
