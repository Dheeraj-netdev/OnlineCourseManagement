using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserService.Contracts;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(UserManager users, TokenIssuer tokens) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await users.RegisterAsync(request, cancellationToken);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.",
                detail: "An account with this email address already exists.");
        }
        return CreatedAtAction(nameof(UsersController.GetMe), "Users", null, UserResponse.FromUser(user));
    }

    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.AuthenticateAsync(request, cancellationToken);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.",
                detail: "The email address or password is incorrect.");
        }
        return Ok(tokens.Issue(user));
    }
}
