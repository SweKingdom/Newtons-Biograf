using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NewtonsBiograf.Api.Data;

namespace NewtonsBiograf.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}

public class TokenService(IOptions<JwtOptions> options)
{
    public const string PermissionClaimType = Permissions.ClaimType;
    public const string RoleClaimType = "role";

    /// <summary>Creates an HS256 JWT with sub, email, name, role and one permission claim per permission.</summary>
    public (string Token, DateTime ExpiresAt) Create(ApplicationUser user, string role, IEnumerable<string> permissions)
    {
        var jwt = options.Value;
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(jwt.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            // Written under the short JWT name "role"; the bearer setup reads it as the role claim.
            new(RoleClaimType, role)
        };
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaimType, p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, now, expires, credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
