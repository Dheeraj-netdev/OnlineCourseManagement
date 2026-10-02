namespace UserService.Authentication;

public sealed class ServiceAuthenticationOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length < 16)
        {
            throw new InvalidOperationException("ServiceAuthentication:ApiKey must be configured with at least 16 characters. Use environment variables or a secret store outside Development.");
        }
    }
}
