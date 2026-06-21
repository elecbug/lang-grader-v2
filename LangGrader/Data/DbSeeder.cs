namespace LangGrader.Data;

using LangGrader.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher<Student>>();

        if (!await db.Students.AnyAsync(s => s.StudentNo == "admin"))
        {
            var admin = new Student
            {
                StudentNo = "admin",
                Name = "Admin",
                Role = "Admin",
                IsActive = true,
                MustChangePassword = false
            };

            admin.PasswordHash = passwordHasher.HashPassword(admin, "admin1234!");

            db.Students.Add(admin);
        }

        if (!await db.Students.AnyAsync(s => s.StudentNo == "20201234"))
        {
            var student = new Student
            {
                StudentNo = "20201234",
                Name = "Test Student",
                Role = "Student",
                IsActive = true,
                MustChangePassword = false
            };

            student.PasswordHash = passwordHasher.HashPassword(student, "test1234!");

            db.Students.Add(student);
        }

        if (!await db.Assignments.AnyAsync())
        {
            db.Assignments.Add(new Assignment
            {
                Title = "HW-13",
                Description = "Test of Submit to GitHub URL",
                OpenAt = DateTime.UtcNow.AddDays(-1),
                DeadlineAt = DateTime.UtcNow.AddDays(7),
                IsPublished = true,
                IsFrozen = false,
                RequiredFilesJson = "[\"main.c\"]",
                MainFileCandidatesJson = "[\"main.c\", \"assignment13.c\"]"
            });
        }

        await db.SaveChangesAsync();
    }
}