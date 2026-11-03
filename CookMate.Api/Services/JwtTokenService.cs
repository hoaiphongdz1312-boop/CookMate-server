using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CookMate.Api.Entities;
using Microsoft.IdentityModel.Tokens;

namespace CookMate.Api.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "CookMate";
    public string Audience { get; set; } = "CookMate.App";
    public int ExpiresDays { get; set; } = 7;

    // Đọc từ user-secrets, không ghi vào appsettings
    public string Key { get; set; } = "";

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public class JwtTokenService(JwtOptions options)
{
    public string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(options.ExpiresDays),
            signingCredentials: new SigningCredentials(options.SigningKey(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
