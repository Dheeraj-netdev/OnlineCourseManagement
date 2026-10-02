using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

var userUrl = Environment.GetEnvironmentVariable("USER_SERVICE_URL") ?? "http://localhost:5101";
var courseUrl = Environment.GetEnvironmentVariable("COURSE_SERVICE_URL") ?? "http://localhost:5102";
using var users = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    { BaseAddress = new Uri(userUrl), Timeout = TimeSpan.FromSeconds(10) };
using var courses = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    { BaseAddress = new Uri(courseUrl), Timeout = TimeSpan.FromSeconds(10) };
try
{
    if (args.Contains("--check-ports"))
    {
        foreach (var port in new[] { new Uri(userUrl).Port, new Uri(courseUrl).Port })
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        return 0;
    }

    await WaitForHealth(users, "User Service");
    await WaitForHealth(courses, "Course Service");
    if (args.Contains("--wait")) return 0;

    var suffix = Guid.NewGuid().ToString("N")[..12];
    const string password = "ReviewPass2026!";
    var instructor = await Register("Ada Instructor", "ada", "Instructor");
    var otherInstructor = await Register("Grace Instructor", "grace", "Instructor");
    var student = await Register("Alex Student", "alex", "Student");
    var otherStudent = await Register("Sam Student", "sam", "Student");
    var instructorToken = await Login("ada");
    var studentToken = await Login("alex");
    var otherStudentToken = await Login("sam");
    var start = new DateTimeOffset(2030, 1, 10, 9, 0, 0, TimeSpan.Zero);
    var end = start.AddDays(2);
    object Course(string title, Guid teacher, DateTimeOffset finish) => new
    {
        title, description = "Created by the live end-to-end review script.",
        startDate = start, endDate = finish, instructorId = teacher
    };

    await Send(courses, HttpMethod.Get, "/api/courses", null, null, HttpStatusCode.Unauthorized, "Anonymous course access rejected");
    await Send(courses, HttpMethod.Post, "/api/courses", Course("Forbidden", instructor, end), studentToken, HttpStatusCode.Forbidden, "Students cannot create courses");
    await Send(courses, HttpMethod.Post, "/api/courses", Course("Unknown teacher", Guid.NewGuid(), end), instructorToken, HttpStatusCode.BadRequest, "Unknown instructor rejected across services");
    await Send(courses, HttpMethod.Post, "/api/courses", Course("Wrong role", student, end), instructorToken, HttpStatusCode.BadRequest, "Student cannot be assigned as instructor");
    await Send(courses, HttpMethod.Post, "/api/courses", Course("Invalid dates", otherInstructor, start), instructorToken, HttpStatusCode.BadRequest, "End date must follow start date");
    var created = await Send(courses, HttpMethod.Post, "/api/courses", Course("Distributed .NET", otherInstructor, end), instructorToken, HttpStatusCode.Created, "Course created for another existing instructor");
    var courseId = created!.Value.GetProperty("id").GetGuid();
    var second = await Send(courses, HttpMethod.Post, "/api/courses", Course("Async APIs", instructor, end), instructorToken, HttpStatusCode.Created, "Second course created");
    var secondId = second!.Value.GetProperty("id").GetGuid();
    var empty = await Send(courses, HttpMethod.Get, "/api/courses", null, studentToken, HttpStatusCode.OK, "Unenrolled student can request their empty list");
    Assert(empty!.Value.GetProperty("totalCount").GetInt32() == 0, "Unenrolled courses are hidden");
    await Send(courses, HttpMethod.Get, $"/api/courses/{courseId}", null, studentToken, HttpStatusCode.NotFound, "Direct lookup hides unenrolled course");
    await Send(courses, HttpMethod.Post, $"/api/courses/{courseId}/enrollments/me", null, studentToken, HttpStatusCode.NoContent, "Student self-enrollment");
    await Send(courses, HttpMethod.Post, $"/api/courses/{courseId}/enrollments/me", null, studentToken, HttpStatusCode.NoContent, "Self-enrollment is idempotent");
    await Send(courses, HttpMethod.Post, $"/api/courses/{secondId}/enrollments", new { studentId = student }, instructorToken, HttpStatusCode.NoContent, "Instructor enrolls a student");
    await Send(courses, HttpMethod.Post, $"/api/courses/{secondId}/enrollments", new { studentId = otherStudent }, studentToken, HttpStatusCode.Forbidden, "Student cannot enroll another student");
    var listed = await Send(courses, HttpMethod.Get, "/api/courses?pageNumber=1&pageSize=1", null, studentToken, HttpStatusCode.OK, "Paginated enrolled list");
    Assert(listed!.Value.GetProperty("totalCount").GetInt32() == 2 && listed.Value.GetProperty("items").GetArrayLength() == 1 && listed.Value.GetProperty("totalPages").GetInt32() == 2, "Pagination metadata and page size");
    var isolated = await Send(courses, HttpMethod.Get, "/api/courses", null, otherStudentToken, HttpStatusCode.OK, "Second student's list");
    Assert(isolated!.Value.GetProperty("totalCount").GetInt32() == 0, "Enrollment isolation between students");
    var search = await Send(courses, HttpMethod.Get, "/api/courses/search?instructorName=gRaCe&startDate=2030-01-11T00%3A00%3A00Z&endDate=2030-01-13T00%3A00%3A00Z", null, studentToken, HttpStatusCode.OK, "Date overlap and case-insensitive instructor search");
    Assert(search!.Value.GetProperty("totalCount").GetInt32() == 1 && search.Value.GetProperty("items")[0].GetProperty("id").GetGuid() == courseId, "Search returns only the matching enrolled course");
    await Send(courses, HttpMethod.Put, $"/api/courses/{courseId}", Course("Distributed .NET Updated", otherInstructor, end), instructorToken, HttpStatusCode.OK, "Instructor updates course");
    var fetched = await Send(courses, HttpMethod.Get, $"/api/courses/{courseId}", null, studentToken, HttpStatusCode.OK, "Enrolled student reads updated course");
    Assert(fetched!.Value.GetProperty("title").GetString() == "Distributed .NET Updated", "Update is persisted");
    await Send(courses, HttpMethod.Delete, $"/api/courses/{courseId}", null, studentToken, HttpStatusCode.Forbidden, "Student cannot delete course");
    await Send(courses, HttpMethod.Delete, $"/api/courses/{courseId}", null, instructorToken, HttpStatusCode.NoContent, "Instructor deletes course");
    await Send(courses, HttpMethod.Get, $"/api/courses/{courseId}", null, studentToken, HttpStatusCode.NotFound, "Deleted course is no longer visible");
    await Send(courses, HttpMethod.Delete, $"/api/courses/{secondId}", null, instructorToken, HttpStatusCode.NoContent, "Demo courses cleaned up");
    Console.WriteLine("\nPASS: Live end-to-end review completed against both independent services.");
    return 0;

    async Task<Guid> Register(string name, string alias, string role)
    {
        var result = await Send(users, HttpMethod.Post, "/api/auth/register", new { name, email = $"{alias}.{suffix}@example.test", password, role }, null, HttpStatusCode.Created, $"Register {role}: {name}");
        return result!.Value.GetProperty("id").GetGuid();
    }

    async Task<string> Login(string alias)
    {
        var result = await Send(users, HttpMethod.Post, "/api/auth/login", new { email = $"{alias}.{suffix}@example.test", password }, null, HttpStatusCode.OK, $"Login {alias} and issue JWT");
        return result!.Value.GetProperty("accessToken").GetString()!;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 1;
}

static async Task WaitForHealth(HttpClient client, string name)
{
    for (var attempt = 0; attempt < 40; attempt++)
    {
        try
        {
            using var response = await client.GetAsync("/health");
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"READY: {name} at {client.BaseAddress}");
                return;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        await Task.Delay(500);
    }
    throw new InvalidOperationException($"{name} did not become healthy. Check .run logs and the configured URL.");
}

static async Task<JsonElement?> Send(HttpClient client, HttpMethod method, string path, object? body, string? token, HttpStatusCode expected, string label)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null) request.Content = JsonContent.Create(body);
    if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (response.StatusCode != expected)
        throw new InvalidOperationException($"{label}: expected {(int)expected}, received {(int)response.StatusCode}: {text}");
    Console.WriteLine($"PASS: {label} ({(int)response.StatusCode})");
    if (string.IsNullOrWhiteSpace(text)) return null;
    using var document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine($"PASS: {label}");
}
