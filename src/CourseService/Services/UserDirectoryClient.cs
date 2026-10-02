using System.Net;
using System.Text.Json;

namespace CourseService.Services;

public sealed class UserDirectoryClient(HttpClient httpClient) : IUserDirectoryClient
{
    public async Task<DirectoryUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"internal/users/{userId:D}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new DirectoryUnavailableException($"User directory returned HTTP {(int)response.StatusCode}.");

            DirectoryUser? user;
            try
            {
                user = await response.Content.ReadFromJsonAsync<DirectoryUser>(cancellationToken);
            }
            catch (InvalidOperationException exception) when (exception.InnerException is ArgumentException)
            {
                // ReadFromJsonAsync wraps an unsupported Content-Type charset in
                // InvalidOperationException. Limit translation to response decoding
                // so unrelated client configuration errors retain their identity.
                throw new DirectoryUnavailableException("User directory returned an invalid response character set.", exception);
            }
            if (user is null || user.Id != userId || string.IsNullOrWhiteSpace(user.Name)
                || user.Name.Length > 100 || string.IsNullOrWhiteSpace(user.Email)
                || user.Email.Length > 254 || user.Role is not ("Student" or "Instructor"))
            {
                throw new DirectoryUnavailableException("User directory returned an invalid user response.");
            }

            return user;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DirectoryUnavailableException("User directory request timed out.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException or IOException)
        {
            throw new DirectoryUnavailableException("User directory could not be reached or returned an invalid response.", exception);
        }
    }
}
