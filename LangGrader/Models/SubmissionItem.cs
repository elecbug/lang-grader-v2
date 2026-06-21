namespace LangGrader.Models;

public class SubmissionItem
{
    public long Id { get; set; }

    public long SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;

    public string OriginalUrl { get; set; } = "";

    // Fields to be filled later by a GitHub URL parser
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string Path { get; set; } = "";

    // Will be extended to Repository, Folder, File, etc.
    public string UrlKind { get; set; } = "Unknown";

    // Main file path specified by the student
    public string? MainFilePath { get; set; }

    public string Status { get; set; } = "PendingValidation";

    public string? ObservedSha { get; set; }
    public string? FinalSha { get; set; }

    public string? SnapshotPath { get; set; }

    public DateTime? ValidatedAt { get; set; }
    public DateTime? FrozenAt { get; set; }

    public List<SubmissionEvent> Events { get; set; } = new();
}