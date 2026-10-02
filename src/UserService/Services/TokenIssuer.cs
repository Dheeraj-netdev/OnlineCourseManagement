using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UserService.Authentication;
using UserService.Contracts;
using UserService.Models;

namespace UserService.Services;

public sealed class TokenIssuer(JwtOptions options)
{
    public LoginResponse Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(options.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim("name", user.Name),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, now.UtcDateTime,
            expires.UtcDateTime, credentials);
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires, UserResponse.FromUser(user));
    }
}
