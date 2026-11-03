using System.ComponentModel.DataAnnotations;

namespace CookMate.Api.Dtos;

public record PostIngredientRequest(string? Name, decimal Quantity, string? Unit, bool IsOptional);

public record PostStepRequest(string? Instruction, int TimerSeconds);

// Dùng cho cả tạo và sửa bài. Kiểm tra giới hạn ở PostValidator (trả 400 { error }).
public record SavePostRequest(
    string? Name,
    string? Description,
    string? ImageUrl,
    int Difficulty,
    int PrepMinutes,
    int CookMinutes,
    int BaseServings,
    int CategoryId,
    List<PostIngredientRequest>? Ingredients,
    List<PostStepRequest>? Steps);

public record CommunityPostDto(
    int Id,
    string Name,
    string Description,
    string? ImageUrl,
    int Difficulty,
    int PrepMinutes,
    int CookMinutes,
    int CategoryId,
    int? AuthorId,
    string? AuthorName,
    DateTime? PublishedAt);

// Bài của tôi: Status 0 chờ duyệt, 1 đã duyệt, 2 bị từ chối
public record MyPostDto(
    int Id,
    string Name,
    string Description,
    string? ImageUrl,
    int Difficulty,
    int PrepMinutes,
    int CookMinutes,
    int CategoryId,
    int Status,
    string? RejectReason,
    DateTime? SubmittedAt);

public record UploadImageResponse(string ImageUrl);

public record PendingPostDto(
    int Id,
    string Name,
    string? ImageUrl,
    int AuthorId,
    string AuthorName,
    DateTime SubmittedAt);

public record PendingCountDto(int Count);

public record RejectRequest(
    [Required(ErrorMessage = "Vui lòng nhập lý do từ chối")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Lý do phải từ 5 đến 500 ký tự")]
    string Reason);

// Reason: một trong ReportReasons.All. Detail bắt buộc khi Reason = "other".
public record ReportRequest(string? Reason, string? Detail);

public record BlockedUserDto(int Id, string FullName, DateTime BlockedAt);

public record ReportDetailDto(int Id, string Reason, string? Detail, string ReporterName, DateTime CreatedAt);

// Một dòng trong danh sách báo cáo của admin: gộp các báo cáo chưa xử lý theo bài
public record ReportedRecipeDto(
    int RecipeId,
    string Name,
    string? ImageUrl,
    int? AuthorId,
    string? AuthorName,
    int ReportCount,
    DateTime LatestReportAt,
    List<ReportDetailDto> Reports);

public record TakedownRequest(
    [Required(ErrorMessage = "Vui lòng nhập lý do gỡ bài")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Lý do phải từ 5 đến 500 ký tự")]
    string Reason);

public record NotificationDto(
    int Id,
    string Type,
    int? RecipeId,
    string Title,
    string Body,
    bool IsRead,
    DateTime CreatedAt);

public record UnreadCountDto(int Count);
