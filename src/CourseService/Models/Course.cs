namespace CourseService.Models;

public sealed class Course
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public Guid InstructorId { get; set; }
    // A snapshot avoids a synchronous cross-service dependency for every course read.
    public string InstructorName { get; set; } = string.Empty;
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}
