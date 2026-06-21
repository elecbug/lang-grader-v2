namespace LangGrader.Models;

public class Submission
{
    public long Id { get; set; }

    public long AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;

    public long StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public string Status { get; set; } = "PendingValidation";

    public bool IsLate { get; set; } = false;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public List<SubmissionItem> Items { get; set; } = new();
}