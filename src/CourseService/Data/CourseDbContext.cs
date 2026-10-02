using CourseService.Models;
using Microsoft.EntityFrameworkCore;

namespace CourseService.Data;

public sealed class CourseDbContext(DbContextOptions<CourseDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(course =>
        {
            course.HasKey(x => x.Id);
            course.Property(x => x.Title).IsRequired().HasMaxLength(200);
            course.Property(x => x.Description).IsRequired().HasMaxLength(2000);
            course.Property(x => x.InstructorName).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Enrollment>(enrollment =>
        {
            enrollment.HasKey(x => new { x.CourseId, x.UserId });
            enrollment.HasIndex(x => x.UserId);
            enrollment.HasOne(x => x.Course)
                .WithMany(x => x.Enrollments)
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
