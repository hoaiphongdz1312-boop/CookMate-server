namespace CookMate.Api.Services;

public static class RateLimitPolicies
{
    // 10 request/phút/IP cho login và face-login (KE_HOACH_v2 mục 3.3)
    public const string Login = "login";

    // Mặc định 5 bài chia sẻ/giờ/người dùng (KE_HOACH_CHIA_SE_CONG_THUC mục 3.5)
    public const string SharePost = "share-post";
}
