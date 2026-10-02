namespace CourseService.Services;

public sealed record DirectoryUser(Guid Id, string Name, string Email, string Role);

public interface IUserDirectoryClient
{
    Task<DirectoryUser?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
