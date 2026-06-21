using LangGrader.Models;

namespace LangGrader.Services;

public sealed class AssignmentSubmissionSummary
{
    public Assignment Assignment { get; init; } = null!;

    public List<SubmissionSummaryRow> Rows { get; init; } = new();

    public int TotalStudents => Rows.Count;

    public int SubmittedStudents => Rows.Count(r => r.LatestSubmission is not null);

    public int ReadyStudents => Rows.Count(r => r.IsReadyForFreeze);

    public int MissingStudents => Rows.Count(r => r.LatestSubmission is null);
}

public sealed class SubmissionSummaryRow
{
    public long StudentId { get; init; }

    public string StudentNo { get; init; } = "";

    public string StudentName { get; init; } = "";

    public int TotalSubmissionCount { get; init; }

    public int DeletedSubmissionCount { get; init; }

    public Submission? LatestSubmission { get; init; }

    public Submission? EffectiveSubmission { get; init; }

    public string EffectiveStatus { get; init; } = "Missing";

    public string Reason { get; init; } = "";

    public bool IsReadyForFreeze { get; init; }

    public bool HasLateSubmission { get; init; }

    public bool HasValidationFailure { get; init; }
}