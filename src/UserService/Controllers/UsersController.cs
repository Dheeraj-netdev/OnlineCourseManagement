using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserService.Contracts;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(UserManager users) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetMe(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var id) || id == Guid.Empty)
        {
            return Unauthorized();
        }
        var user = await users.FindAsync(id, cancellationToken);
        return user is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.")
            : Ok(UserResponse.FromUser(user));
    }
}
