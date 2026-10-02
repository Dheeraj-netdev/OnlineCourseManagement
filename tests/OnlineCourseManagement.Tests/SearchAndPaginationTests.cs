using System.Net;

namespace OnlineCourseManagement.Tests;

public sealed class SearchAndPaginationTests
{
    [Fact]
    public async Task CourseDatesNormalizeToUtcAndSearchComparesEquivalentOffsetInstants()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var start = new DateTimeOffset(2030, 1, 10, 17, 30, 0, TimeSpan.FromHours(5.5));
        var course = await system.CreateCourseAsync(instructor, start: start, end: start.AddDays(2));
        Assert.Equal(TimeSpan.Zero, course.StartDate.Offset);
        Assert.Equal(TimeSpan.Zero, course.EndDate.Offset);
        Assert.Equal(new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero), course.StartDate);
        await system.EnrollAsync(student, course.Id);
        // 07:00 at UTC-05:00 is exactly the course's 12:00 UTC start boundary.
        var matching = await system.ListAsync(student, "/search?endDate=2030-01-10T07%3A00%3A00-05%3A00");
        Assert.Equal(course.Id, Assert.Single(matching.Items).Id);
        var before = await system.ListAsync(student, "/search?endDate=2030-01-10T06%3A59%3A59-05%3A00");
        Assert.Empty(before.Items);
    }

    [Fact]
    public async Task SearchUsesInclusiveDateOverlapAndCaseInsensitivePartialInstructorName()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor", "Ada Lovelace");
        var otherInstructor = await system.RegisterAsync("Instructor", "Grace Hopper");
        var student = await system.RegisterAsync();
        var course = await system.CreateCourseAsync(instructor);
        var differentInstructor = await system.CreateCourseAsync(otherInstructor);
        var unenrolled = await system.CreateCourseAsync(instructor);
        await system.EnrollAsync(student, course.Id);
        await system.EnrollAsync(student, differentInstructor.Id);
        var byName = await system.ListAsync(student, "/search?instructorName=lOvELa");
        Assert.Equal(course.Id, Assert.Single(byName.Items).Id);
        Assert.DoesNotContain(byName.Items, item => item.Id == unenrolled.Id);
        var touchingStart = await system.ListAsync(student,
            "/search?instructorName=ada&startDate=2030-01-01T12%3A00%3A00Z&endDate=2030-01-10T12%3A00%3A00Z");
        Assert.Equal(course.Id, Assert.Single(touchingStart.Items).Id);
        var touchingEnd = await system.ListAsync(student,
            "/search?instructorName=ada&startDate=2030-01-20T12%3A00%3A00Z&endDate=2030-02-01T12%3A00%3A00Z");
        Assert.Equal(course.Id, Assert.Single(touchingEnd.Items).Id);
        var after = await system.ListAsync(student, "/search?startDate=2030-01-20T12%3A00%3A01Z");
        Assert.Empty(after.Items);
        var before = await system.ListAsync(student, "/search?endDate=2030-01-10T11%3A59%3A59Z");
        Assert.Empty(before.Items);
    }

    [Fact]
    public async Task PaginationCountsOnlyEnrolledCoursesAndUsesStableStartDateThenIdOrder()
    {
        using var system = new TestSystem();
        var instructor = await system.RegisterAsync("Instructor");
        var student = await system.RegisterAsync();
        var expected = new List<CourseView>();
        var baseDate = new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 5; index++)
        {
            var start = index == 4 ? baseDate.AddDays(-1) : baseDate;
            var course = await system.CreateCourseAsync(instructor, title: $"Course {index}", start: start, end: start.AddDays(2));
            await system.EnrollAsync(student, course.Id);
            expected.Add(course);
        }
        await system.CreateCourseAsync(instructor, title: "Not enrolled");
        var first = await system.ListAsync(student, "?pageNumber=1&pageSize=2");
        var second = await system.ListAsync(student, "?pageNumber=2&pageSize=2");
        var third = await system.ListAsync(student, "?pageNumber=3&pageSize=2");
        var beyond = await system.ListAsync(student, "?pageNumber=4&pageSize=2");
        Assert.Equal(1, first.PageNumber);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Empty(beyond.Items);
        var actualIds = first.Items.Concat(second.Items).Concat(third.Items).Select(course => course.Id).ToArray();
        Assert.Equal(expected.OrderBy(course => course.StartDate).ThenBy(course => course.Id).Select(course => course.Id).ToArray(), actualIds);
        Assert.Equal(first.Items.Select(course => course.Id), (await system.ListAsync(student, "?pageNumber=1&pageSize=2")).Items.Select(course => course.Id));
        var defaultPage = await system.ListAsync(student);
        Assert.Equal(1, defaultPage.PageNumber);
        Assert.Equal(10, defaultPage.PageSize);
        var searched = await system.ListAsync(student, "/search?instructorName=test&pageNumber=2&pageSize=2");
        Assert.Equal(5, searched.TotalCount);
        Assert.Equal(second.Items.Select(course => course.Id), searched.Items.Select(course => course.Id));
    }

    [Theory]
    [InlineData("?pageNumber=0")]
    [InlineData("?pageNumber=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=-1")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageNumber=not-a-number")]
    [InlineData("/search?pageNumber=0")]
    [InlineData("/search?pageSize=101")]
    [InlineData("/search?startDate=2030-02-01&endDate=2030-01-01")]
    [InlineData("/search?startDate=invalid")]
    public async Task InvalidQueryValuesReturnBadRequest(string query)
    {
        using var system = new TestSystem();
        var student = await system.RegisterAsync();
        using var response = await system.SendCourseAsync(HttpMethod.Get, "/api/courses" + query, student);
        await TestSystem.ExpectAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyCollectionHasConsistentPaginationMetadata()
    {
        using var system = new TestSystem();
        var student = await system.RegisterAsync();
        var result = await system.ListAsync(student);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(10, result.PageSize);
    }
}
