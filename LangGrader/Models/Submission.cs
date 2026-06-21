namespace LangGrader.Models;

public class Submission
{
    public long Id { get; set; }

    public long AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;

    public long StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // Will be extended to include PendingValidation, Valid, Invalid, Frozen, etc.
    public string Status { get; set; } = "PendingValidation";

    public bool IsLate { get; set; } = false;

    public List<SubmissionItem> Items { get; set; } = new();
}