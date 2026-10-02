using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;

namespace OnlineCourseManagement.Tests;

/// <summary>
/// Hosts both real APIs, authentication middleware, EF stores and controllers.
/// Only the HTTP transport between services is redirected into UserService's TestServer.
/// Each test owns fresh databases and does not share mutable server state.
/// </summary>
internal sealed class TestSystem : IDisposable
{
    internal const string Issuer = "OnlineCourseManagement.UserService";
    internal const string Audience = "OnlineCourseManagement";
    internal const string SigningKey = "test-signing-key-at-least-thirty-two-bytes-long-for-hs256";
    internal const string ServiceKey = "test-service-key";
    internal const string Password = "StrongPassword123!";

    public WebApplicationFactory<UserService.Program> Users { get; }
    public WebApplicationFactory<CourseService.Program> Courses { get; }
    public HttpClient UserClient { get; }
    public HttpClient CourseClient { get; }

    public TestSystem(Func<HttpMessageHandler>? directoryHandler = null)
    {
        Users = new WebApplicationFactory<UserService.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Configuration()));
        });
        UserClient = Users.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        Courses = new WebApplicationFactory<CourseService.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Configuration()));
            builder.ConfigureServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(httpBuilder =>
                    httpBuilder.PrimaryHandler = directoryHandler?.Invoke() ?? Users.Server.CreateHandler())));
        });
        CourseClient = Courses.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    }

    private static Dictionary<string, string?> Configuration() => new()
    {
        ["Database:Name"] = Guid.NewGuid().ToString("N"),
        ["Jwt:Issuer"] = Issuer,
        ["Jwt:Audience"] = Audience,
        ["Jwt:SigningKey"] = SigningKey,
        ["Jwt:ExpiryMinutes"] = "60",
        ["ServiceAuthentication:ApiKey"] = ServiceKey,
        ["UserService:BaseUrl"] = "http://localhost"
    };

    public async Task<Identity> RegisterAsync(string role = "Student", string? name = null)
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        using var registered = await UserClient.PostAsJsonAsync("/api/auth/register",
            new { name = name ?? $"Test {role}", email, password = Password, role });
        await ExpectAsync(registered, HttpStatusCode.Created);
        using var loggedIn = await UserClient.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        await ExpectAsync(loggedIn, HttpStatusCode.OK);
        var login = await ReadAsync<LoginView>(loggedIn);
        Assert.Equal("Bearer", login.TokenType);
        Assert.True(login.ExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        return new Identity(login.User.Id, login.User.Name, login.User.Email, login.User.Role, login.AccessToken);
    }

    public Task<HttpResponseMessage> SendCourseAsync(HttpMethod method, string path, Identity? user = null, object? body = null)
        => SendAsync(CourseClient, method, path, user?.AccessToken, body);

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public async Task<CourseView> CreateCourseAsync(Identity instructor, Guid? assignedInstructor = null,
        string title = "Distributed systems", DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        using var response = await SendCourseAsync(HttpMethod.Post, "/api/courses", instructor,
            CourseBody(assignedInstructor ?? instructor.Id, title, start, end));
        await ExpectAsync(response, HttpStatusCode.Created);
        return await ReadAsync<CourseView>(response);
    }

    public static object CourseBody(Guid instructorId, string title = "Distributed systems",
        DateTimeOffset? start = null, DateTimeOffset? end = null) => new
    {
        title,
        description = "An introduction to reliable services.",
        startDate = start ?? new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero),
        endDate = end ?? new DateTimeOffset(2030, 1, 20, 12, 0, 0, TimeSpan.Zero),
        instructorId
    };

    public async Task EnrollAsync(Identity student, Guid courseId)
    {
        using var response = await SendCourseAsync(HttpMethod.Post, $"/api/courses/{courseId}/enrollments/me", student);
        await ExpectAsync(response, HttpStatusCode.NoContent);
    }

    public async Task<CoursePage> ListAsync(Identity user, string query = "")
    {
        using var response = await SendCourseAsync(HttpMethod.Get, "/api/courses" + query, user);
        await ExpectAsync(response, HttpStatusCode.OK);
        return await ReadAsync<CoursePage>(response);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>()) ?? throw new Xunit.Sdk.XunitException("Expected JSON response body.");

    public static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    public static string SignToken(Guid? subject = null, string role = "Student", string? issuer = null,
        string? audience = null, string? signingKey = null, bool expired = false, string? rawSubject = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience,
            [new Claim("sub", rawSubject ?? (subject ?? Guid.NewGuid()).ToString()),
             new Claim("role", role), new Claim("name", "Signed test user"), new Claim("email", "signed@example.test")],
            notBefore: now.AddMinutes(-10), expires: expired ? now.AddMinutes(-2) : now.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        CourseClient.Dispose();
        Courses.Dispose();
        UserClient.Dispose();
        Users.Dispose();
    }
}

internal sealed record Identity(Guid Id, string Name, string Email, string Role, string AccessToken);
internal sealed record UserView(Guid Id, string Name, string Email, string Role);
internal sealed record LoginView(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, UserView User);
internal sealed record CourseView(Guid Id, string Title, string Description, DateTimeOffset StartDate,
    DateTimeOffset EndDate, Guid InstructorId, string InstructorName);
internal sealed record CoursePage(List<CourseView> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages);

internal sealed class ConstantDirectoryHandler(HttpStatusCode status, string? body = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null) response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return Task.FromResult(response);
    }
}
