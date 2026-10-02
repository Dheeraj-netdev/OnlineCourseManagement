using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace UserService.Authentication;

public static class ServiceApiKeyDefaults
{
    public const string AuthenticationScheme = "ServiceApiKey";
    public const string HeaderName = "X-Service-Key";
}

public sealed class ServiceApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServiceAuthenticationOptions serviceOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ServiceApiKeyDefaults.HeaderName, out var values) || values.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Compare equal-sized digests so the key comparison does not reveal a matching prefix.
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(values[0] ?? string.Empty));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(serviceOptions.ApiKey));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid service credential."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "CourseService")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
