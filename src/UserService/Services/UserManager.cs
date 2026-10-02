using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UserService.Contracts;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

public sealed class UserWriteLock : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}

public sealed class UserManager(UserDbContext db, IPasswordHasher<User> passwordHasher, UserWriteLock writeLock)
{
    public async Task<User?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        // EF InMemory does not enforce unique indexes. Serialize the check and insert
        // across requests; a persistent database should also enforce the unique index.
        await writeLock.Semaphore.WaitAsync(cancellationToken);
        try
        {
            if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
            {
                return null;
            }
            var user = new User
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Email = email,
                NormalizedEmail = normalizedEmail,
                Role = request.Role
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.Users.AddAsync(user, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        finally
        {
            writeLock.Semaphore.Release();
        }
    }

    public async Task<User?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
            == PasswordVerificationResult.Failed)
        {
            return null;
        }
        return user;
    }

    public Task<User?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
}
