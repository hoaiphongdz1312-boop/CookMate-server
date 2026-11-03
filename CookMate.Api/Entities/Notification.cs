namespace CookMate.Api.Entities;

public static class NotificationTypes
{
    public const string PostApproved = "post_approved";
    public const string PostRejected = "post_rejected";
    public const string PostRemoved = "post_removed";
}

// Thông báo trong app (app hỏi định kỳ). Chưa có push qua FCM.
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Type { get; set; } = "";
    public int? RecipeId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
