using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Application.Models;

namespace MyApp.Infrastructure.Security;

/// <summary>Выпуск JWT с идентификатором пользователя, правами и меткой сессии.</summary>
public sealed class JwtTokenProvider(JwtOptions options) : ITokenProvider
{
    /// <summary>Выпускает JWT с правами пользователя и текущей меткой сессии.</summary>
    public string Create(User user)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(UserAccess.SessionStampClaim, user.SessionStamp.ToString()),
                ..UserAccess.EffectivePermissions(user)
                    .Select(permission => new Claim("permission", permission))
            ],
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
