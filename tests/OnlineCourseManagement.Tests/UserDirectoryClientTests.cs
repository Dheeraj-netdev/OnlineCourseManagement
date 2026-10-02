using System.Net;
using System.Net.Http.Json;
using CourseService.Services;

namespace OnlineCourseManagement.Tests;

public sealed class UserDirectoryClientTests
{
    [Fact]
    public async Task NotFoundDirectoryResponseMeansMissingUser()
    {
        using var http = new HttpClient(new ConstantDirectoryHandler(HttpStatusCode.NotFound))
            { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        Assert.Null(await client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task DirectoryCannotSubstituteAUserWithDifferentId()
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = Guid.NewGuid(), name = "Wrong user", email = "user@example.test", role = "Instructor" })
        }))) { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task TransportFailureIsTranslatedToDirectoryUnavailable()
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => throw new HttpRequestException("Connection refused")))
            { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidCharsetIsTranslatedToDirectoryUnavailable()
    {
        using var http = new HttpClient(new InvalidCharsetDirectoryHandler())
            { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(
            () => client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task TransportInvalidOperationIsNotMisreportedAsDirectoryFailure()
    {
        var failure = new InvalidOperationException("HTTP handler configuration failure.");
        using var http = new HttpClient(new DelegateHandler((_, _) => throw failure))
            { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task DirectoryTimeoutIsTranslatedToDirectoryUnavailable()
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => throw new TaskCanceledException("Simulated transport timeout")))
            { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => client.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellationIsPreservedRatherThanReportedAsDependencyFailure()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var http = new HttpClient(new DelegateHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Request should already be canceled.");
        })) { BaseAddress = new Uri("http://directory.test/") };
        var client = new UserDirectoryClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetUserAsync(Guid.NewGuid(), canceled.Token));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handle(request, cancellationToken);
    }
}

internal sealed class InvalidCharsetDirectoryHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = Guid.Parse(request.RequestUri!.Segments[^1]),
            name = "Directory Instructor",
            email = "instructor@example.test",
            role = "Instructor"
        });
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        content.Headers.ContentType!.CharSet = "unsupported-charset";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
