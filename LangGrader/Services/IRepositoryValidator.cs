using LangGrader.Models;

namespace LangGrader.Services;

public interface IRepositoryValidator
{
    Task<RepositoryValidationResult> ValidateAsync(
        SubmissionItem item,
        Assignment? assignment = null);
}