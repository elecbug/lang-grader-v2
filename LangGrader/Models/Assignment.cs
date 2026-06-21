namespace LangGrader.Models;

public class Assignment
{
    public long Id { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    public DateTime OpenAt { get; set; }
    public DateTime DeadlineAt { get; set; }

    public bool IsPublished { get; set; } = false;
    public bool IsFrozen { get; set; } = false;

    public string RequiredFilesJson { get; set; } = "[]";
    public string MainFileCandidatesJson { get; set; } = "[\"main.c\"]";

    public List<Submission> Submissions { get; set; } = new();
}