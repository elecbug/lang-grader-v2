namespace LangGrader.Services;

public sealed class GitHubUrlParseResult
{
    public bool IsValid { get; init; }

    public string? ErrorMessage { get; init; }

    public string OriginalUrl { get; init; } = "";

    public string Owner { get; init; } = "";
    public string Repo { get; init; } = "";
    public string Branch { get; init; } = "main";
    public string Path { get; init; } = "";

    public string UrlKind { get; init; } = "Unknown";

    public string NormalizedUrl { get; init; } = "";

    public static GitHubUrlParseResult Invalid(string originalUrl, string errorMessage)
    {
        return new GitHubUrlParseResult
        {
            IsValid = false,
            OriginalUrl = originalUrl,
            ErrorMessage = errorMessage
        };
    }
}