using CookMate.Api.Data;
using CookMate.Api.Entities;

namespace CookMate.Api.Services;

// Tạo thông báo cho tác giả khi bài được duyệt, bị từ chối hoặc bị gỡ. Chỉ Add vào context,
// người gọi SaveChanges cùng lúc với thay đổi trạng thái bài.
public static class Notifier
{
    public static void PostReviewed(AppDbContext db, Recipe recipe, DateTime now)
    {
        if (recipe.AuthorId == null) return;
        var (type, title, body) = recipe.Status == RecipeStatus.Approved
            ? (NotificationTypes.PostApproved, "Bài đã được duyệt",
                $"\"{recipe.Name}\" đã hiện ở tab Cộng đồng.")
            : (NotificationTypes.PostRejected, "Bài bị từ chối",
                $"\"{recipe.Name}\" bị từ chối: {recipe.RejectReason}");
        Add(db, recipe, type, title, body, now);
    }

    public static void PostRemoved(AppDbContext db, Recipe recipe, DateTime now)
    {
        if (recipe.AuthorId == null) return;
        Add(db, recipe, NotificationTypes.PostRemoved, "Bài bị gỡ",
            $"\"{recipe.Name}\" bị gỡ khỏi tab Cộng đồng: {recipe.RejectReason}", now);
    }

    private static void Add(AppDbContext db, Recipe recipe, string type, string title, string body, DateTime now) =>
        db.Notifications.Add(new Notification
        {
            UserId = recipe.AuthorId!.Value,
            Type = type,
            RecipeId = recipe.Id,
            Title = title,
            Body = body,
            CreatedAt = now
        });
}
