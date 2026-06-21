namespace LangGrader.Services;

public sealed class AssignmentFreezeResult
{
    public long AssignmentId { get; init; }

    public int SelectedSubmissionCount { get; set; }

    public int FrozenSubmissionCount { get; set; }

    public int FailedSubmissionCount { get; set; }

    public List<string> Messages { get; } = new();

    public bool HasFailures => FailedSubmissionCount > 0;
}