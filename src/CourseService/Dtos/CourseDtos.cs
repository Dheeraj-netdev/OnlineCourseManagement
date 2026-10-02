using System.ComponentModel.DataAnnotations;

namespace CourseService.Dtos;

public sealed record CourseResponse(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid InstructorId,
    string InstructorName);

public sealed class CourseRequest : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(2000)]
    public string Description { get; init; } = string.Empty;

    public DateTimeOffset StartDate { get; init; }
    public DateTimeOffset EndDate { get; init; }
    public Guid InstructorId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate == default)
            yield return new ValidationResult("StartDate is required.", [nameof(StartDate)]);
        if (EndDate == default)
            yield return new ValidationResult("EndDate is required.", [nameof(EndDate)]);
        if (EndDate <= StartDate)
            yield return new ValidationResult("EndDate must occur after StartDate.", [nameof(EndDate)]);
        if (InstructorId == Guid.Empty)
            yield return new ValidationResult("InstructorId must be a nonempty GUID.", [nameof(InstructorId)]);
    }
}

public sealed class EnrollStudentRequest : IValidatableObject
{
    public Guid StudentId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StudentId == Guid.Empty)
            yield return new ValidationResult("StudentId must be a nonempty GUID.", [nameof(StudentId)]);
    }
}

public class PaginationQuery
{
    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 10;
}

public sealed class CourseSearchQuery : PaginationQuery, IValidatableObject
{
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }

    [StringLength(100)]
    public string? InstructorName { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate.HasValue && EndDate.HasValue && EndDate.Value < StartDate.Value)
            yield return new ValidationResult("EndDate must be on or after StartDate.", [nameof(EndDate)]);
    }
}

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);
