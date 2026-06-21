using LangGrader.Models;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionItem> SubmissionItems => Set<SubmissionItem>();
    public DbSet<SubmissionEvent> SubmissionEvents => Set<SubmissionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Student>()
            .HasIndex(s => s.StudentNo)
            .IsUnique();

        modelBuilder.Entity<Assignment>()
            .HasMany(a => a.Submissions)
            .WithOne(s => s.Assignment)
            .HasForeignKey(s => s.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Student>()
            .HasMany(s => s.Submissions)
            .WithOne(s => s.Student)
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Submission>()
            .HasMany(s => s.Items)
            .WithOne(i => i.Submission)
            .HasForeignKey(i => i.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubmissionItem>()
            .HasMany(i => i.Events)
            .WithOne(e => e.SubmissionItem)
            .HasForeignKey(e => e.SubmissionItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}