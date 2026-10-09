using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace MyApp.API.Extensions
{
    public static class JwtExtensions
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
        {
            var jwt = config.GetRequiredSection("Jwt");
            var keyValue = jwt.GetValue<string>("Key");
            if (string.IsNullOrWhiteSpace(keyValue))
            {
                throw new InvalidOperationException(
                    "Configuration value 'Jwt:Key' must be provided and must not be empty.");
            }
            if (Encoding.UTF8.GetByteCount(keyValue) < 32)
            {
                throw new InvalidOperationException(
                    "Configuration value 'Jwt:Key' must contain at least 32 bytes.");
            }
            var issuer = jwt.GetValue<string>("Issuer") ?? throw new InvalidOperationException("Configuration value 'Jwt:Issuer' is required.");
            var audience = jwt.GetValue<string>("Audience") ?? throw new InvalidOperationException("Configuration value 'Jwt:Audience' is required.");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                {
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = key
                    };
                });

            return services;
        }
    }
}
