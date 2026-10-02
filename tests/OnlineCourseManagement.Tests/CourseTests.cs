using System.Net;
using CourseService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineCourseManagement.Tests;

public sealed class CourseTests
{
    [Fact]
    public async Task InstructorCanCreateForAnotherInstructorAndUpdateAndDeleteCourse()
    {
        using var system = new TestSystem();
        var owner = await system.RegisterAsync("Instructor", "Author");
        var colleague = await system.RegisterAsync("Instructor", "Teaching colleague");
        var course = await system.CreateCourseAsync(owner, colleague.Id);
        Assert.Equal(colleague.Id, course.InstructorId);
        Assert.Equal(colleague.Name, course.InstructorName);
        await system.EnrollAsync(owner, course.Id);
        using var update = await system.SendCourseAsync(HttpMethod.Put, $"/api/courses/{course.Id}", colleague,
            TestSystem.CourseBody(owner.Id, "Updated course"));
        await TestSystem.ExpectAsync(update, HttpStatusCode.OK);
        var updated = await TestSystem.ReadAsync<CourseView>(update);
        Assert.Equal("Updated course", updated.Title);
        Assert.Equal(owner.Id, updated.InstructorId);
        Assert.Equal(owner.Name, updated.InstructorName);
        using var read = await system.SendCourseAsync(HttpMethod.Get, $"/api/courses/{course.Id}", owner);
        await TestSystem.ExpectAsync(read, HttpStatusCode.OK);
        Assert.Equal("Updated course", (await TestSystem.ReadAsync<CourseView>(read)).Title);
        using var deleted = await system.SendCourseAsync(HttpMethod.Delete, $"/api/courses/{course.Id}", colleague);
        await TestSystem.ExpectAsync(deleted, HttpStatusCode.NoContent);
        using var missing = await system.SendCourseAsync(HttpMethod.Get, $"/api/courses/{course.Id}", owner);
        await TestSystem.ExpectAsync(missing, HttpStatusCode.NotFound);
        using var scope = system.Courses.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CourseDbContext>();
        Assert.Equal(0, await db.Courses.CountAsync());
        Assert.Equal(0, await db.Enrollments.CountAsync());
    }

    [Fact]
    public async Task StudentCannotCreateUpdateDeleteOrEnrollAnotherStudent()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        var operations = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/courses", TestSystem.CourseBody(instructor.Id)),
            (HttpMethod.Put, $"/api/courses/{course.Id}", TestSystem.CourseBody(instructor.Id)),
            (HttpMethod.Delete, $"/api/courses/{course.Id}", null),
            (HttpMethod.Post, $"/api/courses/{course.Id}/enrollments", new { studentId = student.Id })
        };
        foreach (var operation in operations)
        {
            using var response = await system.SendCourseAsync(operation.Method, operation.Path, student, operation.Body);
            await TestSystem.ExpectAsync(response, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task ListingSearchAndGetByIdNeverExposeUnenrolledCourses()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var alice = await system.RegisterAsync(name: "Alice");
        var bob = await system.RegisterAsync(name: "Bob");
        var aliceCourse = await system.CreateCourseAsync(instructor, title: "Alice course");
        var bobCourse = await system.CreateCourseAsync(instructor, title: "Bob course");
        await system.EnrollAsync(alice, aliceCourse.Id);
        await system.EnrollAsync(bob, bobCourse.Id);
        foreach (var userAndCourse in new[] { (User: alice, Visible: aliceCourse, Hidden: bobCourse), (User: bob, Visible: bobCourse, Hidden: aliceCourse) })
        {
            var page = await system.ListAsync(userAndCourse.User);
            Assert.Equal(userAndCourse.Visible.Id, Assert.Single(page.Items).Id);
            Assert.Equal(1, page.TotalCount);
            var search = await system.ListAsync(userAndCourse.User, "/search?instructorName=test");
            Assert.Equal(userAndCourse.Visible.Id, Assert.Single(search.Items).Id);
            using var denied = await system.SendCourseAsync(HttpMethod.Get, $"/api/courses/{userAndCourse.Hidden.Id}", userAndCourse.User);
            await TestSystem.ExpectAsync(denied, HttpStatusCode.NotFound);
        }
        Assert.Empty((await system.ListAsync(instructor)).Items);
        using var instructorCannotBypass = await system.SendCourseAsync(HttpMethod.Get, $"/api/courses/{aliceCourse.Id}", instructor);
        await TestSystem.ExpectAsync(instructorCannotBypass, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InstructorCanEnrollAnExistingStudentIdempotently()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await system.SendCourseAsync(HttpMethod.Post, $"/api/courses/{course.Id}/enrollments",
                instructor, new { studentId = student.Id });
            await TestSystem.ExpectAsync(response, HttpStatusCode.NoContent);
        }
        Assert.Equal(course.Id, Assert.Single((await system.ListAsync(student)).Items).Id);
        using var scope = system.Courses.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CourseDbContext>().Enrollments.CountAsync());
    }

    [Fact]
    public async Task ConcurrentSelfAndInstructorEnrollmentCreateOnlyOneEnrollment()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => index % 2 == 0
            ? system.SendCourseAsync(HttpMethod.Post, $"/api/courses/{course.Id}/enrollments/me", student)
            : system.SendCourseAsync(HttpMethod.Post, $"/api/courses/{course.Id}/enrollments", instructor, new { studentId = student.Id })));
        try
        {
            foreach (var response in responses) await TestSystem.ExpectAsync(response, HttpStatusCode.NoContent);
            Assert.Equal(1, (await system.ListAsync(student)).TotalCount);
            using var scope = system.Courses.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CourseDbContext>().Enrollments.CountAsync());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task MissingOrStudentInstructorIsRejectedOnCreateAndUpdate()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        foreach (var invalidId in new[] { Guid.NewGuid(), student.Id, Guid.Empty })
        {
            using var create = await system.SendCourseAsync(HttpMethod.Post, "/api/courses", instructor, TestSystem.CourseBody(invalidId));
            using var update = await system.SendCourseAsync(HttpMethod.Put, $"/api/courses/{course.Id}", instructor, TestSystem.CourseBody(invalidId));
            await TestSystem.ExpectAsync(create, HttpStatusCode.BadRequest);
            await TestSystem.ExpectAsync(update, HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task EnrollmentRejectsMissingStudentWrongRoleAndUnknownCourse()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        foreach (var invalidId in new[] { Guid.NewGuid(), instructor.Id, Guid.Empty })
        {
            using var response = await system.SendCourseAsync(HttpMethod.Post, $"/api/courses/{course.Id}/enrollments",
                instructor, new { studentId = invalidId });
            await TestSystem.ExpectAsync(response, HttpStatusCode.BadRequest);
        }
        using var missingCourse = await system.SendCourseAsync(HttpMethod.Post, $"/api/courses/{Guid.NewGuid()}/enrollments/me", student);
        await TestSystem.ExpectAsync(missingCourse, HttpStatusCode.NotFound);
        using var missingUser = await TestSystem.SendAsync(system.CourseClient, HttpMethod.Post,
            $"/api/courses/{course.Id}/enrollments/me", TestSystem.SignToken());
        await TestSystem.ExpectAsync(missingUser, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("reversed")]
    [InlineData("missing")]
    public async Task CourseDatesMustBePresentAndStrictlyIncreasing(string invalidDates)
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var start = new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);
        object body = invalidDates == "missing"
            ? new { title = "Missing dates", description = "Test", instructorId = instructor.Id }
            : TestSystem.CourseBody(instructor.Id, start: start, end: invalidDates == "same" ? start : start.AddDays(-1));
        using var response = await system.SendCourseAsync(HttpMethod.Post, "/api/courses", instructor, body);
        await TestSystem.ExpectAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CourseTitleIsRequired(string? title)
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        using var response = await system.SendCourseAsync(HttpMethod.Post, "/api/courses", instructor,
            new { title, description = "Test", startDate = "2030-01-10T00:00:00Z", endDate = "2030-01-11T00:00:00Z", instructorId = instructor.Id });
        await TestSystem.ExpectAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingCourseUpdateAndDeleteReturnNotFound()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var missingId = Guid.NewGuid();
        using var update = await system.SendCourseAsync(HttpMethod.Put, $"/api/courses/{missingId}", instructor, TestSystem.CourseBody(instructor.Id));
        using var delete = await system.SendCourseAsync(HttpMethod.Delete, $"/api/courses/{missingId}", instructor);
        await TestSystem.ExpectAsync(update, HttpStatusCode.NotFound);
        await TestSystem.ExpectAsync(delete, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(503, null)]
    [InlineData(401, null)]
    [InlineData(200, "not-json")]
    [InlineData(200, "{}")]
    public async Task UnavailableOrMalformedUserDirectoryReturnsServiceUnavailable(int status, string? body)
    {
        using var system = new TestSystem(() => new ConstantDirectoryHandler((HttpStatusCode)status, body));
        var instructor = await system.RegisterAsync("Instructor");
        using var response = await system.SendCourseAsync(HttpMethod.Post, "/api/courses", instructor, TestSystem.CourseBody(instructor.Id));
        await TestSystem.ExpectAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task InvalidDirectoryCharsetReturnsServiceUnavailable()
    {
        using var system = new TestSystem(() => new InvalidCharsetDirectoryHandler());
        var instructor = await system.RegisterAsync("Instructor");
        using var response = await system.SendCourseAsync(HttpMethod.Post, "/api/courses", instructor,
            TestSystem.CourseBody(instructor.Id));
        await TestSystem.ExpectAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var scope = system.Courses.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<CourseDbContext>().Courses.ToListAsync());
    }

    [Theory]
    [InlineData("text/plain", "missing-course", 404)]
    [InlineData("text/html", "missing-course", 404)]
    [InlineData("text/plain", "missing-instructor", 400)]
    [InlineData("text/html", "missing-instructor", 400)]
    [InlineData("text/plain", "directory-unavailable", 503)]
    [InlineData("text/html", "directory-unavailable", 503)]
    public async Task ErrorStatusSurvivesUnsupportedAcceptHeaders(string accept, string failure, int expectedStatus)
    {
        using var system = failure == "directory-unavailable"
            ? new TestSystem(() => new ConstantDirectoryHandler(HttpStatusCode.ServiceUnavailable))
            : new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var deleting = failure == "missing-course";
        using var request = new HttpRequestMessage(deleting ? HttpMethod.Delete : HttpMethod.Post,
            deleting ? $"/api/courses/{Guid.NewGuid()}" : "/api/courses");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", instructor.AccessToken);
        request.Headers.Accept.ParseAdd(accept);
        if (!deleting)
            request.Content = System.Net.Http.Json.JsonContent.Create(TestSystem.CourseBody(
                failure == "missing-instructor" ? Guid.NewGuid() : instructor.Id));
        using var response = await system.CourseClient.SendAsync(request);
        await TestSystem.ExpectAsync(response, (HttpStatusCode)expectedStatus);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
