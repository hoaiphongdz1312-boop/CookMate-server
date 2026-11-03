using System.Text.RegularExpressions;
using CookMate.Api.Dtos;

namespace CookMate.Api.Services;

// Giới hạn nội dung bài chia sẻ (KE_HOACH_CHIA_SE_CONG_THUC mục 3.5). Trả thông báo lỗi đầu tiên, null nếu hợp lệ.
public static partial class PostValidator
{
    public const int MaxName = 100;
    public const int MaxDescription = 1000;
    public const int MaxIngredients = 40;
    public const int MaxSteps = 30;
    public const int MaxStepText = 1000;
    public const int MaxTimerSeconds = 86400;

    // Chỉ nhận ảnh do chính server cấp (POST /api/community/images), không nhận URL bên ngoài
    [GeneratedRegex(@"^/uploads/[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex ImageUrlPattern();

    public static string? Validate(SavePostRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Thiếu tên món";
        if (r.Name.Trim().Length > MaxName) return $"Tên món tối đa {MaxName} ký tự";
        if (string.IsNullOrWhiteSpace(r.Description)) return "Thiếu mô tả";
        if (r.Description.Trim().Length > MaxDescription) return $"Mô tả tối đa {MaxDescription} ký tự";
        if (r.ImageUrl != null && !ImageUrlPattern().IsMatch(r.ImageUrl)) return "Ảnh không hợp lệ";
        if (r.Difficulty is < 1 or > 3) return "Độ khó phải từ 1 đến 3";
        if (r.PrepMinutes < 0 || r.CookMinutes < 0) return "Thời gian không được âm";
        if (r.PrepMinutes > 10080 || r.CookMinutes > 10080) return "Thời gian quá lớn";
        if (r.BaseServings < 1 || r.BaseServings > 20) return "Khẩu phần phải từ 1 đến 20";

        if (r.Ingredients is not { Count: > 0 }) return "Cần ít nhất 1 nguyên liệu";
        if (r.Ingredients.Count > MaxIngredients) return $"Tối đa {MaxIngredients} nguyên liệu";
        foreach (var i in r.Ingredients)
        {
            if (string.IsNullOrWhiteSpace(i.Name)) return "Nguyên liệu thiếu tên";
            if (i.Name.Trim().Length > 200) return "Tên nguyên liệu quá dài";
            if (i.Quantity < 0 || i.Quantity > 99999999) return "Số lượng nguyên liệu không hợp lệ";
            if (string.IsNullOrWhiteSpace(i.Unit)) return "Nguyên liệu thiếu đơn vị";
            if (i.Unit.Trim().Length > 30) return "Đơn vị quá dài";
        }

        if (r.Steps is not { Count: > 0 }) return "Cần ít nhất 1 bước";
        if (r.Steps.Count > MaxSteps) return $"Tối đa {MaxSteps} bước";
        foreach (var s in r.Steps)
        {
            if (string.IsNullOrWhiteSpace(s.Instruction)) return "Có bước chưa nhập nội dung";
            if (s.Instruction.Trim().Length > MaxStepText) return $"Mỗi bước tối đa {MaxStepText} ký tự";
            if (s.TimerSeconds < 0 || s.TimerSeconds > MaxTimerSeconds) return "Thời gian hẹn giờ không hợp lệ";
        }
        return null;
    }
}
