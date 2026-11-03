namespace CookMate.Api.Services;

// AutoApproveAfter: tác giả đã có từ chừng này bài được duyệt trở lên thì bài mới đăng thẳng, không chờ duyệt.
// Đổi bằng Community:AutoApproveAfter trong appsettings.
public record CommunityOptions(int AutoApproveAfter = CommunityOptions.DefaultAutoApproveAfter)
{
    public const int DefaultAutoApproveAfter = 3;
}
