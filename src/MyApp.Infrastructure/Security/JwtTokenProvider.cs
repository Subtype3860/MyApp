using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MyApp.Application.Abstractions;
using MyApp.Application.Security;
using MyApp.Domain.Entities;

namespace MyApp.Infrastructure.Security;

public sealed class JwtTokenProvider(JwtOptions options) : ITokenProvider
{
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
                ..(user.Role.Equals("administrator", StringComparison.OrdinalIgnoreCase)
                    ? Permissions.All
                    : Permissions.Expand(user.Permissions.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                    .Select(permission => new Claim("permission", permission))
            ],
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
