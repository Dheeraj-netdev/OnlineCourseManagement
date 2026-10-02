using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CourseService.Services;

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case CourseRuleException rule:
                problem = new ProblemDetails { Status = rule.StatusCode, Title = rule.Title, Detail = rule.Message };
                break;
            case DirectoryUnavailableException:
                logger.LogWarning(exception, "User directory operation failed.");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "User directory unavailable",
                    Detail = "The user service is temporarily unavailable. Please try again later."
                };
                break;
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return false;
            default:
                logger.LogError(exception, "An unexpected course service error occurred.");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred."
                };
                break;
        }

        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status!.Value;
        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        }))
        {
            // Error handling must preserve the intended status even when the
            // caller's Accept header does not match a registered problem writer.
            await context.Response.WriteAsJsonAsync(
                problem,
                options: (System.Text.Json.JsonSerializerOptions?)null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);
        }
        return true;
    }
}
