namespace LangGrader.Models;

public class Student
{
    public long Id { get; set; }

    public string StudentNo { get; set; } = "";
    public string Name { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    // "Student" or "Admin"
    public string Role { get; set; } = "Student";

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Submission> Submissions { get; set; } = new();
}