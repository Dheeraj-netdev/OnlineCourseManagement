using System.Text;

namespace CourseService.Configuration;

public sealed record ServiceSettings(
    string JwtIssuer,
    string JwtAudience,
    string JwtSigningKey,
    string ApiKey,
    Uri UserServiceBaseUrl)
{
    public static ServiceSettings FromConfiguration(IConfiguration configuration)
    {
        static string Required(IConfiguration config, string key)
        {
            var value = config[key];
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"Configuration '{key}' is required.");
        }

        var signingKey = Required(configuration, "Jwt:SigningKey");
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
        }

        var baseUrl = Required(configuration, "UserService:BaseUrl");
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("UserService:BaseUrl must be an absolute HTTP or HTTPS URL without credentials, a query, or a fragment.");
        }

        return new ServiceSettings(
            Required(configuration, "Jwt:Issuer"),
            Required(configuration, "Jwt:Audience"),
            signingKey,
            Required(configuration, "ServiceAuthentication:ApiKey"),
            baseUri);
    }
}
