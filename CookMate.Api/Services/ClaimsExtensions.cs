using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CookMate.Api.Services;

public static class ClaimsExtensions
{
    // Id người dùng trong token (claim "sub")
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? throw new InvalidOperationException("Token không có claim sub"));
}
