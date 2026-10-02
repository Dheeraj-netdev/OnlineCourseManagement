using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserService.Data;

namespace OnlineCourseManagement.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task RegistrationLoginAndMeUseRealSignedTokensAndNeverExposePasswordMaterial()
    {
        using var system = new TestSystem();
        var user = await system.RegisterAsync(name: "  Ada Lovelace  ");
        using var response = await TestSystem.SendAsync(system.UserClient, HttpMethod.Get, "/api/users/me", user.AccessToken);
        await TestSystem.ExpectAsync(response, HttpStatusCode.OK);
        var me = await TestSystem.ReadAsync<UserView>(response);
        Assert.Equal(user.Id, me.Id);
        Assert.Equal("Ada Lovelace", me.Name);
        Assert.Equal("Student", me.Role);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        using var scope = system.Users.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        var stored = await db.Users.SingleAsync();
        Assert.NotEqual(TestSystem.Password, stored.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(stored.PasswordHash));
    }

    [Fact]
    public async Task RegistrationDefaultsRoleToStudentAndNormalizesIdentity()
    {
        using var system = new TestSystem();
        using var registered = await system.UserClient.PostAsJsonAsync("/api/auth/register",
            new { name = "  Grace Hopper  ", email = "  Grace.Hopper@Example.Test  ", password = TestSystem.Password });
        await TestSystem.ExpectAsync(registered, HttpStatusCode.Created);
        var user = await TestSystem.ReadAsync<UserView>(registered);
        Assert.Equal("Grace Hopper", user.Name);
        Assert.Equal("Student", user.Role);
        using var login = await system.UserClient.PostAsJsonAsync("/api/auth/login",
            new { email = " grace.hopper@example.test ", password = TestSystem.Password });
        await TestSystem.ExpectAsync(login, HttpStatusCode.OK);
        using var duplicate = await system.UserClient.PostAsJsonAsync("/api/auth/register",
            new { name = "Duplicate", email = "GRACE.HOPPER@EXAMPLE.TEST", password = TestSystem.Password });
        await TestSystem.ExpectAsync(duplicate, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistrationsCreateExactlyOneAccount()
    {
        using var system = new TestSystem();
        var body = new { name = "Concurrent user", email = "race@example.test", password = TestSystem.Password, role = "Student" };
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => system.UserClient.PostAsJsonAsync("/api/auth/register", body)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            using var scope = system.Users.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
            Assert.Equal(1, await db.Users.CountAsync());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData(null, "valid@example.test", "StrongPassword123!", "Student")]
    [InlineData("   ", "valid@example.test", "StrongPassword123!", "Student")]
    [InlineData("Test", null, "StrongPassword123!", "Student")]
    [InlineData("Test", "invalid-email", "StrongPassword123!", "Student")]
    [InlineData("Test", "valid@example.test", null, "Student")]
    [InlineData("Test", "valid@example.test", "short1A", "Student")]
    [InlineData("Test", "valid@example.test", "lowercase12345", "Student")]
    [InlineData("Test", "valid@example.test", "UPPERCASE12345", "Student")]
    [InlineData("Test", "valid@example.test", "NoDigitsInPassword", "Student")]
    [InlineData("Test", "valid@example.test", "StrongPassword123!", "Administrator")]
    public async Task InvalidRegistrationIsRejected(string? name, string? email, string? password, string role)
    {
        using var system = new TestSystem();
        using var response = await system.UserClient.PostAsJsonAsync("/api/auth/register", new { name, email, password, role });
        await TestSystem.ExpectAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownEmailAndIncorrectPasswordBothReturnUnauthorized()
    {
        using var system = new TestSystem();
        var user = await system.RegisterAsync();
        foreach (var email in new[] { user.Email, "missing@example.test" })
        {
            using var response = await system.UserClient.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword123!" });
            await TestSystem.ExpectAsync(response, HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task DirectoryRequiresServiceCredentialEvenForAuthenticatedInstructor()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        using var anonymous = await system.UserClient.GetAsync($"/internal/users/{instructor.Id}");
        await TestSystem.ExpectAsync(anonymous, HttpStatusCode.Unauthorized);
        using var bearerOnly = await TestSystem.SendAsync(system.UserClient, HttpMethod.Get,
            $"/internal/users/{instructor.Id}", instructor.AccessToken);
        await TestSystem.ExpectAsync(bearerOnly, HttpStatusCode.Unauthorized);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{instructor.Id}");
        invalidRequest.Headers.Add("X-Service-Key", "wrong-service-key");
        using var invalid = await system.UserClient.SendAsync(invalidRequest);
        await TestSystem.ExpectAsync(invalid, HttpStatusCode.Unauthorized);
        using var validRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{instructor.Id}");
        validRequest.Headers.Add("X-Service-Key", TestSystem.ServiceKey);
        using var valid = await system.UserClient.SendAsync(validRequest);
        await TestSystem.ExpectAsync(valid, HttpStatusCode.OK);
        Assert.Equal(instructor.Id, (await TestSystem.ReadAsync<UserView>(valid)).Id);
        using var missingRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{Guid.NewGuid()}");
        missingRequest.Headers.Add("X-Service-Key", TestSystem.ServiceKey);
        using var missing = await system.UserClient.SendAsync(missingRequest);
        await TestSystem.ExpectAsync(missing, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("subject")]
    [InlineData("empty-subject")]
    [InlineData("malformed")]
    [InlineData("algorithm")]
    [InlineData("role")]
    public async Task InvalidJwtIsRejectedByBothServices(string fault)
    {
        using var system = new TestSystem();
        var token = fault switch
        {
            "expired" => TestSystem.SignToken(expired: true),
            "issuer" => TestSystem.SignToken(issuer: "untrusted-issuer"),
            "audience" => TestSystem.SignToken(audience: "different-audience"),
            "signature" => TestSystem.SignToken(signingKey: "incorrect-signing-key-at-least-thirty-two-bytes-long"),
            "subject" => TestSystem.SignToken(rawSubject: "not-a-guid"),
            "empty-subject" => TestSystem.SignToken(subject: Guid.Empty),
            "algorithm" => TestSystem.SignToken(algorithm: "HS384"),
            "role" => TestSystem.SignToken(role: "Administrator"),
            _ => "this.is.not.a.jwt"
        };
        using var userResponse = await TestSystem.SendAsync(system.UserClient, HttpMethod.Get, "/api/users/me", token);
        await TestSystem.ExpectAsync(userResponse, HttpStatusCode.Unauthorized);
        using var courseResponse = await TestSystem.SendAsync(system.CourseClient, HttpMethod.Get, "/api/courses", token);
        await TestSystem.ExpectAsync(courseResponse, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicHealthEndpointsAreAvailableAndProtectedEndpointsRequireLogin()
    {
        using var system = new TestSystem();
        using var userHealth = await system.UserClient.GetAsync("/health");
        using var courseHealth = await system.CourseClient.GetAsync("/health");
        await TestSystem.ExpectAsync(userHealth, HttpStatusCode.OK);
        await TestSystem.ExpectAsync(courseHealth, HttpStatusCode.OK);
        using var me = await system.UserClient.GetAsync("/api/users/me");
        using var courses = await system.CourseClient.GetAsync("/api/courses");
        await TestSystem.ExpectAsync(me, HttpStatusCode.Unauthorized);
        await TestSystem.ExpectAsync(courses, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidTokenForMissingUserDoesNotInventAProfile()
    {
        using var system = new TestSystem();
        using var response = await TestSystem.SendAsync(system.UserClient, HttpMethod.Get, "/api/users/me", TestSystem.SignToken());
        await TestSystem.ExpectAsync(response, HttpStatusCode.NotFound);
    }
}
