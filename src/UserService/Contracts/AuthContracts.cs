using System.ComponentModel.DataAnnotations;
using UserService.Models;

namespace UserService.Contracts;

public sealed class RegisterRequest : IValidatableObject
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 10)]
    public string Password { get; set; } = string.Empty;

    [Required, RegularExpression("^(Student|Instructor)$", ErrorMessage = "Role must be Student or Instructor.")]
    public string Role { get; set; } = UserRoles.Student;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Email) && !new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            yield return new ValidationResult("A valid email address is required.", [nameof(Email)]);
        }
        if (!string.IsNullOrEmpty(Password)
            && (!Password.Any(char.IsUpper) || !Password.Any(char.IsLower) || !Password.Any(char.IsDigit)))
        {
            yield return new ValidationResult("Password must contain an uppercase letter, a lowercase letter, and a digit.", [nameof(Password)]);
        }
    }
}

public sealed class LoginRequest : IValidatableObject
{
    [Required, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Email) && !new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            yield return new ValidationResult("A valid email address is required.", [nameof(Email)]);
        }
    }
}

public sealed record UserResponse(Guid Id, string Name, string Email, string Role)
{
    public static UserResponse FromUser(User user) => new(user.Id, user.Name, user.Email, user.Role);
}

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, UserResponse User);
