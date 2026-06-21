namespace LangGrader.Models;

public class SubmissionEvent
{
    public long Id { get; set; }

    public long SubmissionItemId { get; set; }
    public SubmissionItem SubmissionItem { get; set; } = null!;

    public string EventType { get; set; } = "";
    public string Message { get; set; } = "";

    public string? RawLogPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}