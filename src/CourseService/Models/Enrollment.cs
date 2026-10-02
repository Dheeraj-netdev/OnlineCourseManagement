namespace CourseService.Models;

public sealed class Enrollment
{
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset EnrolledAtUtc { get; set; }
    public Course Course { get; set; } = null!;
}
