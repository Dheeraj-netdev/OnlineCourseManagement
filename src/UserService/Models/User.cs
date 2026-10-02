namespace UserService.Models;

public static class UserRoles
{
    public const string Student = "Student";
    public const string Instructor = "Instructor";
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.Student;
}
