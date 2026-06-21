namespace LangGrader.Services;

public interface IEffectiveSubmissionSelector
{
    Task<AssignmentSubmissionSummary?> GetAssignmentSummaryAsync(long assignmentId);
}
