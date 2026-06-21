namespace LangGrader.Services;

public interface IAssignmentFreezeService
{
    Task<AssignmentFreezeResult> FreezeAssignmentAsync(long assignmentId);

    Task<AssignmentFreezeResult> UnfreezeAssignmentAsync(long assignmentId);
}