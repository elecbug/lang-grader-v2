using LangGrader.Models;

namespace LangGrader.Services;

public sealed class GitRepositoryValidator : IRepositoryValidator
{
    private readonly ICommandRunner _commandRunner;
    private readonly IWebHostEnvironment _environment;

    public GitRepositoryValidator(
        ICommandRunner commandRunner,
        IWebHostEnvironment environment)
    {
        _commandRunner = commandRunner;
        _environment = environment;
    }

    public async Task<RepositoryValidationResult> ValidateAsync(SubmissionItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Owner) ||
            string.IsNullOrWhiteSpace(item.Repo))
        {
            return RepositoryValidationResult.Fail(
                "InvalidUrl",
                "Owner or repository name is empty."
            );
        }

        var repoUrl = $"https://github.com/{item.Owner}/{item.Repo}.git";

        var branch = item.Branch;

        if (string.IsNullOrWhiteSpace(branch))
        {
            var defaultBranchResult = await ResolveDefaultBranchAsync(repoUrl);

            if (!defaultBranchResult.Success)
            {
                return RepositoryValidationResult.Fail(
                    "DefaultBranchNotFound",
                    defaultBranchResult.Message
                );
            }

            branch = defaultBranchResult.Branch;
        }

        var validationRoot = Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "validation"
        );

        Directory.CreateDirectory(validationRoot);

        var workDir = Path.Combine(
            validationRoot,
            $"item_{item.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}"
        );

        Directory.CreateDirectory(workDir);

        var cloneDir = Path.Combine(workDir, "repo");

        try
        {
            var cloneResult = await _commandRunner.RunAsync(
                "git",
                new[]
                {
                    "clone",
                    "--depth", "1",
                    "--branch", branch,
                    repoUrl,
                    cloneDir
                },
                timeoutSeconds: 60
            );

            if (cloneResult.TimedOut)
            {
                return RepositoryValidationResult.Fail(
                    "CloneTimeout",
                    "Git clone timed out.",
                    branch
                );
            }

            if (cloneResult.ExitCode != 0)
            {
                return RepositoryValidationResult.Fail(
                    "CloneFailed",
                    TrimForDisplay(cloneResult.StandardError),
                    branch
                );
            }

            var shaResult = await _commandRunner.RunAsync(
                "git",
                new[]
                {
                    "rev-parse",
                    "HEAD"
                },
                workingDirectory: cloneDir,
                timeoutSeconds: 10
            );

            var observedSha = shaResult.ExitCode == 0
                ? shaResult.StandardOutput.Trim()
                : null;

            var targetPath = string.IsNullOrWhiteSpace(item.Path)
                ? cloneDir
                : Path.Combine(
                    cloneDir,
                    item.Path.Replace('/', Path.DirectorySeparatorChar)
                );

            if (item.UrlKind is "Repository" or "Folder")
            {
                if (!Directory.Exists(targetPath))
                {
                    return RepositoryValidationResult.Fail(
                        "PathNotFound",
                        $"The submitted folder path does not exist: {DisplayPath(item.Path)}",
                        branch
                    );
                }

                var mainFilePath = string.IsNullOrWhiteSpace(item.MainFilePath)
                    ? "main.c"
                    : item.MainFilePath;

                var absoluteMainFilePath = Path.Combine(
                    targetPath,
                    mainFilePath.Replace('/', Path.DirectorySeparatorChar)
                );

                if (!File.Exists(absoluteMainFilePath))
                {
                    return RepositoryValidationResult.Fail(
                        "MainFileNotFound",
                        $"The main file does not exist: {mainFilePath}",
                        branch
                    );
                }

                return RepositoryValidationResult.Success(
                    $"Repository URL is valid. Resolved branch: {branch}. Folder path and main file were found.",
                    observedSha,
                    targetPath,
                    branch
                );
            }

            if (item.UrlKind is "File" or "RawFile")
            {
                if (!File.Exists(targetPath))
                {
                    return RepositoryValidationResult.Fail(
                        "FileNotFound",
                        $"The submitted file path does not exist: {DisplayPath(item.Path)}",
                        branch
                    );
                }

                return RepositoryValidationResult.Success(
                    $"File URL is valid. Resolved branch: {branch}. The submitted file was found.",
                    observedSha,
                    targetPath,
                    branch
                );
            }

            return RepositoryValidationResult.Fail(
                "UnsupportedUrlKind",
                $"Unsupported URL kind: {item.UrlKind}",
                branch
            );
        }
        catch (Exception ex)
        {
            return RepositoryValidationResult.Fail(
                "SystemError",
                ex.Message,
                branch
            );
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir))
                {
                    Directory.Delete(workDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private async Task<(bool Success, string Branch, string Message)> ResolveDefaultBranchAsync(string repoUrl)
    {
        var result = await _commandRunner.RunAsync(
            "git",
            new[]
            {
                "ls-remote",
                "--symref",
                repoUrl,
                "HEAD"
            },
            timeoutSeconds: 30
        );

        if (result.TimedOut)
        {
            return (false, "", "Resolving the default branch timed out.");
        }

        if (result.ExitCode != 0)
        {
            return (
                false,
                "",
                $"Failed to resolve the default branch: {TrimForDisplay(result.StandardError)}"
            );
        }

        var lines = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            const string prefix = "ref: refs/heads/";

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[prefix.Length..];

            var tabIndex = rest.IndexOf('\t');
            var spaceIndex = rest.IndexOf(' ');

            var cutIndex = tabIndex >= 0 && spaceIndex >= 0
                ? Math.Min(tabIndex, spaceIndex)
                : Math.Max(tabIndex, spaceIndex);

            var branch = cutIndex >= 0
                ? rest[..cutIndex]
                : rest;

            if (!string.IsNullOrWhiteSpace(branch))
            {
                return (true, branch.Trim(), "Default branch resolved.");
            }
        }

        return (false, "", "The default branch could not be found from remote HEAD.");
    }

    private static string DisplayPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "/" : path;
    }

    private static string TrimForDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No error message was returned.";
        }

        text = text.Trim();

        return text.Length <= 1000
            ? text
            : text[..1000] + "...";
    }
}