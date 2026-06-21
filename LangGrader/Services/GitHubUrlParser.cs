namespace LangGrader.Services;

public interface IGitHubUrlParser
{
    GitHubUrlParseResult Parse(string input);
}

public sealed class GitHubUrlParser : IGitHubUrlParser
{
    public GitHubUrlParseResult Parse(string input)
    {
        var originalUrl = input?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return GitHubUrlParseResult.Invalid(originalUrl, "URL is empty.");
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
        {
            return GitHubUrlParseResult.Invalid(originalUrl, "URL is not a valid absolute URL.");
        }

        var host = uri.Host.ToLowerInvariant();

        if (host is "github.com" or "www.github.com")
        {
            return ParseGitHubWebUrl(originalUrl, uri);
        }

        if (host == "raw.githubusercontent.com")
        {
            return ParseRawGitHubUrl(originalUrl, uri);
        }

        return GitHubUrlParseResult.Invalid(
            originalUrl,
            "Only github.com or raw.githubusercontent.com URLs are supported."
        );
    }

    private static GitHubUrlParseResult ParseGitHubWebUrl(string originalUrl, Uri uri)
    {
        var segments = GetSegments(uri);

        if (segments.Length < 2)
        {
            return GitHubUrlParseResult.Invalid(
                originalUrl,
                "GitHub URL must include owner and repository name."
            );
        }

        var owner = segments[0];
        var repo = RemoveGitSuffix(segments[1]);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return GitHubUrlParseResult.Invalid(
                originalUrl,
                "Owner or repository name is empty."
            );
        }

        if (segments.Length == 2)
        {
            return CreateValidResult(
                originalUrl,
                owner,
                repo,
                branch: "main",
                path: "",
                urlKind: "Repository"
            );
        }

        var marker = segments[2];

        if (marker == "tree")
        {
            if (segments.Length < 4)
            {
                return GitHubUrlParseResult.Invalid(
                    originalUrl,
                    "Folder URL must include a branch name after /tree/."
                );
            }

            var branch = segments[3];
            var path = JoinPath(segments.Skip(4));

            return CreateValidResult(
                originalUrl,
                owner,
                repo,
                branch,
                path,
                "Folder"
            );
        }

        if (marker == "blob")
        {
            if (segments.Length < 5)
            {
                return GitHubUrlParseResult.Invalid(
                    originalUrl,
                    "File URL must include a branch name and file path after /blob/."
                );
            }

            var branch = segments[3];
            var path = JoinPath(segments.Skip(4));

            return CreateValidResult(
                originalUrl,
                owner,
                repo,
                branch,
                path,
                "File"
            );
        }

        return GitHubUrlParseResult.Invalid(
            originalUrl,
            "Unsupported GitHub URL format. Use repository, folder, or file URL."
        );
    }

    private static GitHubUrlParseResult ParseRawGitHubUrl(string originalUrl, Uri uri)
    {
        var segments = GetSegments(uri);

        if (segments.Length < 4)
        {
            return GitHubUrlParseResult.Invalid(
                originalUrl,
                "Raw GitHub URL must include owner, repository, branch, and file path."
            );
        }

        var owner = segments[0];
        var repo = RemoveGitSuffix(segments[1]);
        var branch = segments[2];
        var path = JoinPath(segments.Skip(3));

        if (string.IsNullOrWhiteSpace(path))
        {
            return GitHubUrlParseResult.Invalid(
                originalUrl,
                "Raw GitHub URL must point to a file."
            );
        }

        return CreateValidResult(
            originalUrl,
            owner,
            repo,
            branch,
            path,
            "RawFile"
        );
    }

    private static GitHubUrlParseResult CreateValidResult(
        string originalUrl,
        string owner,
        string repo,
        string branch,
        string path,
        string urlKind)
    {
        var normalizedUrl = string.IsNullOrWhiteSpace(path)
            ? $"https://github.com/{owner}/{repo}"
            : $"https://github.com/{owner}/{repo}/tree/{branch}/{path}";

        return new GitHubUrlParseResult
        {
            IsValid = true,
            OriginalUrl = originalUrl,
            Owner = owner,
            Repo = repo,
            Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch,
            Path = path,
            UrlKind = urlKind,
            NormalizedUrl = normalizedUrl
        };
    }

    private static string[] GetSegments(Uri uri)
    {
        return uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
    }

    private static string RemoveGitSuffix(string repo)
    {
        return repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repo[..^4]
            : repo;
    }

    private static string JoinPath(IEnumerable<string> segments)
    {
        return string.Join("/", segments.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}