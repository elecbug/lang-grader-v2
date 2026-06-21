namespace LangGrader.Services;

public sealed class RepositoryValidationResult
{
    public bool IsValid { get; init; }

    public string Status { get; init; } = "Unknown";

    public string Message { get; init; } = "";

    public string? ObservedSha { get; init; }

    public string? LocalPath { get; init; }

    public string? ResolvedBranch { get; init; }

    public static RepositoryValidationResult Success(
        string message,
        string? observedSha,
        string? localPath,
        string? resolvedBranch)
    {
        return new RepositoryValidationResult
        {
            IsValid = true,
            Status = "Valid",
            Message = message,
            ObservedSha = observedSha,
            LocalPath = localPath,
            ResolvedBranch = resolvedBranch
        };
    }

    public static RepositoryValidationResult Success(
        string status,
        string message,
        string? observedSha,
        string? localPath,
        string? resolvedBranch)
    {
        return new RepositoryValidationResult
        {
            IsValid = true,
            Status = status,
            Message = message,
            ObservedSha = observedSha,
            LocalPath = localPath,
            ResolvedBranch = resolvedBranch
        };
    }

    public static RepositoryValidationResult Fail(
        string status,
        string message)
    {
        return new RepositoryValidationResult
        {
            IsValid = false,
            Status = status,
            Message = message
        };
    }

    public static RepositoryValidationResult Fail(
        string status,
        string message,
        string? resolvedBranch)
    {
        return new RepositoryValidationResult
        {
            IsValid = false,
            Status = status,
            Message = message,
            ResolvedBranch = resolvedBranch
        };
    }

    public static RepositoryValidationResult Fail(
        string status,
        string message,
        string? observedSha,
        string? localPath,
        string? resolvedBranch)
    {
        return new RepositoryValidationResult
        {
            IsValid = false,
            Status = status,
            Message = message,
            ObservedSha = observedSha,
            LocalPath = localPath,
            ResolvedBranch = resolvedBranch
        };
    }
}