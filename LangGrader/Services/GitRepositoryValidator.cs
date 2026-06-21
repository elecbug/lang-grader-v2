using LangGrader.Models;
using System.Text.Json;

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

    public async Task<RepositoryValidationResult> ValidateAsync(
        SubmissionItem item,
        Assignment? assignment = null)
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
                        $"Submitted folder path was not found: {DisplayPath(item.Path)}",
                        observedSha: observedSha,
                        localPath: targetPath,
                        resolvedBranch: branch
                    );
                }

                var mainFileName = string.IsNullOrWhiteSpace(item.MainFilePath)
                    ? "main.c"
                    : item.MainFilePath.Trim();

                var mainFilePath = Path.Combine(
                    targetPath,
                    mainFileName.Replace('/', Path.DirectorySeparatorChar)
                );

                if (!File.Exists(mainFilePath))
                {
                    return RepositoryValidationResult.Fail(
                        "MainFileNotFound",
                        $"Main file was not found: {mainFileName}",
                        observedSha: observedSha,
                        localPath: targetPath,
                        resolvedBranch: branch
                    );
                }

                var requiredFilesResult = CheckRequiredFilesForDirectory(
                    targetPath,
                    assignment
                );

                if (!requiredFilesResult.IsValid)
                {
                    return RepositoryValidationResult.Fail(
                        requiredFilesResult.Status,
                        requiredFilesResult.Message,
                        observedSha: observedSha,
                        localPath: targetPath,
                        resolvedBranch: branch
                    );
                }

                return RepositoryValidationResult.Success(
                    status: "Valid",
                    message: $"Repository was validated successfully. Branch={branch}, SHA={observedSha}.",
                    observedSha: observedSha,
                    localPath: targetPath,
                    resolvedBranch: branch
                );
            }
            else if (item.UrlKind is "File" or "RawFile")
            {
                if (!File.Exists(targetPath))
                {
                    return RepositoryValidationResult.Fail(
                        "FileNotFound",
                        $"Submitted file path was not found: {DisplayPath(item.Path)}",
                        observedSha: observedSha,
                        localPath: targetPath,
                        resolvedBranch: branch
                    );
                }

                var requiredFilesResult = CheckRequiredFilesForSingleFile(
                    targetPath,
                    item,
                    assignment
                );

                if (!requiredFilesResult.IsValid)
                {
                    return RepositoryValidationResult.Fail(
                        requiredFilesResult.Status,
                        requiredFilesResult.Message,
                        observedSha: observedSha,
                        localPath: targetPath,
                        resolvedBranch: branch
                    );
                }

                return RepositoryValidationResult.Success(
                    status: "Valid",
                    message: $"File was validated successfully. Branch={branch}, SHA={observedSha}.",
                    observedSha: observedSha,
                    localPath: targetPath,
                    resolvedBranch: branch
                );
            }
            else
            {
                return RepositoryValidationResult.Fail(
                    "UnsupportedUrlKind",
                    $"Unsupported URL kind: {item.UrlKind}",
                    observedSha: observedSha,
                    localPath: targetPath,
                    resolvedBranch: branch
                );
            }
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

    private sealed class RequiredFilesCheckResult
    {
        public bool IsValid { get; init; }

        public string Status { get; init; } = "Valid";

        public string Message { get; init; } = "";
    }

    private static RequiredFilesCheckResult CheckRequiredFilesForDirectory(
        string baseDirectory,
        Assignment? assignment)
    {
        var parseResult = ParseRequiredFiles(assignment);

        if (!parseResult.IsValid)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = false,
                Status = "RequiredFileConfigInvalid",
                Message = parseResult.ErrorMessage
            };
        }

        if (parseResult.RequiredFiles.Count == 0)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = true,
                Status = "Valid",
                Message = "No required files were configured."
            };
        }

        var missingFiles = new List<string>();

        foreach (var requiredFile in parseResult.RequiredFiles)
        {
            var candidatePath = Path.Combine(
                baseDirectory,
                requiredFile.Replace('/', Path.DirectorySeparatorChar)
            );

            if (!File.Exists(candidatePath))
            {
                missingFiles.Add(requiredFile);
            }
        }

        if (missingFiles.Count > 0)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = false,
                Status = "RequiredFileMissing",
                Message = "Required file(s) missing: " + string.Join(", ", missingFiles)
            };
        }

        return new RequiredFilesCheckResult
        {
            IsValid = true,
            Status = "Valid",
            Message = "All required files were found."
        };
    }

    private static RequiredFilesCheckResult CheckRequiredFilesForSingleFile(
        string submittedFilePath,
        SubmissionItem item,
        Assignment? assignment)
    {
        var parseResult = ParseRequiredFiles(assignment);

        if (!parseResult.IsValid)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = false,
                Status = "RequiredFileConfigInvalid",
                Message = parseResult.ErrorMessage
            };
        }

        if (parseResult.RequiredFiles.Count == 0)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = true,
                Status = "Valid",
                Message = "No required files were configured."
            };
        }

        if (parseResult.RequiredFiles.Count > 1)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = false,
                Status = "RequiredFileMissing",
                Message = "This assignment requires multiple files, but the submission URL points to a single file. Required file(s): " +
                          string.Join(", ", parseResult.RequiredFiles)
            };
        }

        var requiredFile = parseResult.RequiredFiles[0];

        var submittedFileName = Path.GetFileName(submittedFilePath);
        var submittedPath = item.Path?.Replace('\\', '/') ?? "";

        var matchesByFileName = string.Equals(
            submittedFileName,
            Path.GetFileName(requiredFile),
            StringComparison.OrdinalIgnoreCase
        );

        var matchesByPath = submittedPath.EndsWith(
            requiredFile,
            StringComparison.OrdinalIgnoreCase
        );

        if (!matchesByFileName && !matchesByPath)
        {
            return new RequiredFilesCheckResult
            {
                IsValid = false,
                Status = "RequiredFileMissing",
                Message = $"Required file was not satisfied. Required={requiredFile}, Submitted={submittedPath}"
            };
        }

        return new RequiredFilesCheckResult
        {
            IsValid = true,
            Status = "Valid",
            Message = "Required file was found."
        };
    }

    private sealed class RequiredFilesParseResult
    {
        public bool IsValid { get; init; }

        public List<string> RequiredFiles { get; init; } = new();

        public string ErrorMessage { get; init; } = "";
    }

    private static RequiredFilesParseResult ParseRequiredFiles(Assignment? assignment)
    {
        if (assignment is null || string.IsNullOrWhiteSpace(assignment.RequiredFilesJson))
        {
            return new RequiredFilesParseResult
            {
                IsValid = true,
                RequiredFiles = new List<string>()
            };
        }

        List<string>? values;

        try
        {
            values = JsonSerializer.Deserialize<List<string>>(assignment.RequiredFilesJson);
        }
        catch (JsonException)
        {
            return new RequiredFilesParseResult
            {
                IsValid = false,
                ErrorMessage = "Assignment required files JSON is invalid."
            };
        }

        if (values is null)
        {
            return new RequiredFilesParseResult
            {
                IsValid = false,
                ErrorMessage = "Assignment required files JSON must be an array of strings."
            };
        }

        var normalizedFiles = new List<string>();

        foreach (var value in values)
        {
            var normalized = NormalizeRequiredFilePath(value);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!IsSafeRelativeFilePath(normalized))
            {
                return new RequiredFilesParseResult
                {
                    IsValid = false,
                    ErrorMessage = $"Required file path is invalid: {value}"
                };
            }

            normalizedFiles.Add(normalized);
        }

        normalizedFiles = normalizedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RequiredFilesParseResult
        {
            IsValid = true,
            RequiredFiles = normalizedFiles
        };
    }

    private static string NormalizeRequiredFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static bool IsSafeRelativeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return false;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part == "." || part == "..")
            {
                return false;
            }

            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}