using CourseService.Dtos;
using CourseService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourseService.Controllers;

[ApiController]
[Route("api/courses")]
[Authorize]
public sealed class CoursesController(CourseManagementService courses) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<CourseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<CourseResponse>>> List(
        [FromQuery] PaginationQuery query, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
            return Unauthorized();
        return Ok(await courses.ListAsync(callerId, query, null, cancellationToken));
    }

    [HttpGet("search")]
    [ProducesResponseType<PagedResponse<CourseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<CourseResponse>>> Search(
        [FromQuery] CourseSearchQuery query, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
            return Unauthorized();
        return Ok(await courses.ListAsync(callerId, query, query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
            return Unauthorized();
        var course = await courses.GetAsync(id, callerId, cancellationToken);
        return course is null ? NotFound() : Ok(course);
    }

    [HttpPost]
    [Authorize(Roles = "Instructor")]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CourseResponse>> Create(CourseRequest request, CancellationToken cancellationToken)
    {
        var course = await courses.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = course.Id }, course);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Instructor")]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CourseResponse>> Update(Guid id, CourseRequest request, CancellationToken cancellationToken)
        => Ok(await courses.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Instructor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await courses.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/enrollments/me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> EnrollSelf(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
            return Unauthorized();
        await courses.EnrollSelfAsync(id, callerId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/enrollments")]
    [Authorize(Roles = "Instructor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> EnrollStudent(Guid id, EnrollStudentRequest request, CancellationToken cancellationToken)
    {
        await courses.EnrollStudentAsync(id, request.StudentId, cancellationToken);
        return NoContent();
    }

    private bool TryGetCallerId(out Guid id)
        => Guid.TryParse(User.FindFirst("sub")?.Value, out id) && id != Guid.Empty;
}
