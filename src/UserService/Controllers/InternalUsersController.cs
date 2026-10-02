using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserService.Authentication;
using UserService.Contracts;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("internal/users")]
[Authorize(AuthenticationSchemes = ServiceApiKeyDefaults.AuthenticationScheme)]
public sealed class InternalUsersController(UserManager users) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(id, cancellationToken);
        return user is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found.")
            : Ok(UserResponse.FromUser(user));
    }
}
