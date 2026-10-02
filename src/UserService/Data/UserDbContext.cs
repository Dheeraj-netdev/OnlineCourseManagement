using Microsoft.EntityFrameworkCore;
using UserService.Models;

namespace UserService.Data;

public sealed class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();
        user.HasKey(item => item.Id);
        user.Property(item => item.Name).HasMaxLength(100).IsRequired();
        user.Property(item => item.Email).HasMaxLength(254).IsRequired();
        user.Property(item => item.NormalizedEmail).HasMaxLength(254).IsRequired();
        user.Property(item => item.PasswordHash).IsRequired();
        user.Property(item => item.Role).HasMaxLength(20).IsRequired();
        user.HasIndex(item => item.NormalizedEmail).IsUnique();
    }
}
