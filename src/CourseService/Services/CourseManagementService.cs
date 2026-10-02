using System.Linq.Expressions;
using CourseService.Data;
using CourseService.Dtos;
using CourseService.Models;
using Microsoft.EntityFrameworkCore;

namespace CourseService.Services;

public sealed class CourseManagementService(
    CourseDbContext database,
    IUserDirectoryClient users,
    CourseMutationGate mutationGate)
{
    private static readonly Expression<Func<Course, CourseResponse>> ProjectCourse = course => new CourseResponse(
        course.Id, course.Title, course.Description, course.StartDate, course.EndDate,
        course.InstructorId, course.InstructorName);

    public async Task<PagedResponse<CourseResponse>> ListAsync(
        Guid callerId,
        PaginationQuery paging,
        CourseSearchQuery? search,
        CancellationToken cancellationToken)
    {
        // Restrict the query before counting, filtering, or paging. Even aggregate
        // counts must not disclose courses belonging to other students.
        var query = EnrolledCourses(callerId);
        if (search?.StartDate is { } start)
            query = query.Where(course => course.EndDate >= start.ToUniversalTime());
        if (search?.EndDate is { } end)
            query = query.Where(course => course.StartDate <= end.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(search?.InstructorName))
        {
            var name = search.InstructorName.Trim();
            query = query.Where(course => course.InstructorName.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        // Use a long to avoid integer overflow for deliberately huge page numbers.
        var skip = ((long)paging.PageNumber - 1) * paging.PageSize;
        var items = skip >= totalCount
            ? new List<CourseResponse>()
            : await query.OrderBy(course => course.StartDate).ThenBy(course => course.Id)
                .Skip((int)skip).Take(paging.PageSize).Select(ProjectCourse).ToListAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)paging.PageSize);
        return new PagedResponse<CourseResponse>(items, paging.PageNumber, paging.PageSize, totalCount, totalPages);
    }

    public async Task<CourseResponse?> GetAsync(Guid courseId, Guid callerId, CancellationToken cancellationToken)
        => await EnrolledCourses(callerId).Where(course => course.Id == courseId)
            .Select(ProjectCourse).SingleOrDefaultAsync(cancellationToken);

    public async Task<CourseResponse> CreateAsync(CourseRequest request, CancellationToken cancellationToken)
    {
        var instructor = await RequireInstructorAsync(request.InstructorId, cancellationToken);
        await mutationGate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var course = new Course { Id = Guid.NewGuid() };
            Apply(course, request, instructor.Name);
            await database.Courses.AddAsync(course, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return ToResponse(course);
        }
        finally
        {
            mutationGate.Semaphore.Release();
        }
    }

    public async Task<CourseResponse> UpdateAsync(Guid courseId, CourseRequest request, CancellationToken cancellationToken)
    {
        await mutationGate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var course = await RequireCourseAsync(courseId, cancellationToken);
            var instructor = await RequireInstructorAsync(request.InstructorId, cancellationToken);
            Apply(course, request, instructor.Name);
            await database.SaveChangesAsync(cancellationToken);
            return ToResponse(course);
        }
        finally
        {
            mutationGate.Semaphore.Release();
        }
    }

    public async Task DeleteAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await mutationGate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var course = await RequireCourseAsync(courseId, cancellationToken);
            var enrollments = await database.Enrollments.Where(enrollment => enrollment.CourseId == courseId)
                .ToListAsync(cancellationToken);
            database.Enrollments.RemoveRange(enrollments);
            database.Courses.Remove(course);
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Semaphore.Release();
        }
    }

    public Task EnrollSelfAsync(Guid courseId, Guid callerId, CancellationToken cancellationToken)
        => EnrollAsync(courseId, callerId, requireStudent: false, cancellationToken);

    public Task EnrollStudentAsync(Guid courseId, Guid studentId, CancellationToken cancellationToken)
        => EnrollAsync(courseId, studentId, requireStudent: true, cancellationToken);

    private IQueryable<Course> EnrolledCourses(Guid callerId)
        => database.Courses.AsNoTracking()
            .Where(course => course.Enrollments.Any(enrollment => enrollment.UserId == callerId));

    private async Task EnrollAsync(Guid courseId, Guid userId, bool requireStudent, CancellationToken cancellationToken)
    {
        await mutationGate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await RequireCourseAsync(courseId, cancellationToken);
            var user = await users.GetUserAsync(userId, cancellationToken);
            if (user is null)
            {
                throw new CourseRuleException(
                    requireStudent ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound,
                    requireStudent ? "Invalid student" : "User not found",
                    requireStudent ? "The requested student does not exist." : "The authenticated user no longer exists.");
            }
            if (requireStudent && user.Role != "Student")
                throw new CourseRuleException(StatusCodes.Status400BadRequest, "Invalid student", "The requested user must have the Student role.");

            if (await database.Enrollments.AnyAsync(
                enrollment => enrollment.CourseId == courseId && enrollment.UserId == userId, cancellationToken))
                return;

            await database.Enrollments.AddAsync(new Enrollment
            {
                CourseId = courseId,
                UserId = userId,
                EnrolledAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            mutationGate.Semaphore.Release();
        }
    }

    private async Task<Course> RequireCourseAsync(Guid courseId, CancellationToken cancellationToken)
        => await database.Courses.SingleOrDefaultAsync(course => course.Id == courseId, cancellationToken)
            ?? throw new CourseRuleException(StatusCodes.Status404NotFound, "Course not found", "The requested course does not exist.");

    private async Task<DirectoryUser> RequireInstructorAsync(Guid instructorId, CancellationToken cancellationToken)
    {
        var instructor = await users.GetUserAsync(instructorId, cancellationToken);
        if (instructor is null)
            throw new CourseRuleException(StatusCodes.Status400BadRequest, "Invalid instructor", "The requested instructor does not exist.");
        if (instructor.Role != "Instructor")
            throw new CourseRuleException(StatusCodes.Status400BadRequest, "Invalid instructor", "The requested user must have the Instructor role.");
        return instructor;
    }

    private static void Apply(Course course, CourseRequest request, string instructorName)
    {
        course.Title = request.Title.Trim();
        course.Description = request.Description.Trim();
        course.StartDate = request.StartDate.ToUniversalTime();
        course.EndDate = request.EndDate.ToUniversalTime();
        course.InstructorId = request.InstructorId;
        course.InstructorName = instructorName;
    }

    private static CourseResponse ToResponse(Course course)
        => new(course.Id, course.Title, course.Description, course.StartDate, course.EndDate,
            course.InstructorId, course.InstructorName);
}
