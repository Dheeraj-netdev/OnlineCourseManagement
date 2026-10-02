using System.Net;
using System.Text.Json;

namespace OnlineCourseManagement.Tests;

public sealed class DocumentationTests
{
    [Fact]
    public async Task CourseOpenApiDocumentsAnonymousHealthAndProtectedCourseOperations()
    {
        using var system = new TestSystem();
        using var response = await system.CourseClient.GetAsync("/swagger/v1/swagger.json");
        await TestSystem.ExpectAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("security", out _));

        var paths = root.GetProperty("paths");
        var health = paths.GetProperty("/health").GetProperty("get");
        Assert.False(health.TryGetProperty("security", out _));
        var protectedOperations = new (string Path, string Method)[]
        {
            ("/api/courses", "get"),
            ("/api/courses", "post"),
            ("/api/courses/search", "get"),
            ("/api/courses/{id}", "get"),
            ("/api/courses/{id}", "put"),
            ("/api/courses/{id}", "delete"),
            ("/api/courses/{id}/enrollments/me", "post"),
            ("/api/courses/{id}/enrollments", "post")
        };
        foreach (var (path, method) in protectedOperations)
        {
            var security = paths.GetProperty(path).GetProperty(method).GetProperty("security");
            var requirement = Assert.Single(security.EnumerateArray());
            Assert.Empty(requirement.GetProperty("Bearer").EnumerateArray());
        }
    }
}
