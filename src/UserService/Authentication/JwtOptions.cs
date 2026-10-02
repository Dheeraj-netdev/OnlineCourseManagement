using System.Text;

namespace UserService.Authentication;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        }
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 UTF-8 bytes. Use environment variables or a secret store outside Development.");
        }
        if (ExpiryMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Jwt:ExpiryMinutes must be between 1 and 1440.");
        }
    }
}
