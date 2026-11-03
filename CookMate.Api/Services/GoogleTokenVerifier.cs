using Google.Apis.Auth;

namespace CookMate.Api.Services;

public class GoogleOptions
{
    // Web client ID (không phải Android client ID)
    public string ClientId { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}

public record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

public interface IGoogleTokenVerifier
{
    // Trả null nếu token sai, hết hạn hoặc không dành cho ứng dụng này
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default);
}

public class GoogleTokenVerifier(GoogleOptions options, ILogger<GoogleTokenVerifier> logger) : IGoogleTokenVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings { Audience = [options.ClientId] };
            var p = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return new GoogleIdentity(p.Subject, p.Email, p.EmailVerified, p.Name);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning("idToken Google không hợp lệ: {Message}", ex.Message);
            return null;
        }
    }
}
