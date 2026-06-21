using System.IO.Compression;
using LangGrader.Data;
using LangGrader.Models;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Services;

public sealed class AssignmentFreezeService : IAssignmentFreezeService
{
    private readonly AppDbContext _db;
    private readonly IEffectiveSubmissionSelector _effectiveSubmissionSelector;
    private readonly ICommandRunner _commandRunner;
    private readonly IWebHostEnvironment _environment;

    public AssignmentFreezeService(
        AppDbContext db,
        IEffectiveSubmissionSelector effectiveSubmissionSelector,
        ICommandRunner commandRunner,
        IWebHostEnvironment environment)
    {
        _db = db;
        _effectiveSubmissionSelector = effectiveSubmissionSelector;
        _commandRunner = commandRunner;
        _environment = environment;
    }

    public async Task<AssignmentFreezeResult> FreezeAssignmentAsync(long assignmentId)
    {
        var result = new AssignmentFreezeResult
        {
            AssignmentId = assignmentId
        };

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null)
        {
            result.Messages.Add("Assignment not found.");
            result.FailedSubmissionCount++;
            return result;
        }

        if (assignment.IsFrozen)
        {
            result.Messages.Add("Assignment is already frozen.");
            return result;
        }

        var summary = await _effectiveSubmissionSelector.GetAssignmentSummaryAsync(assignmentId);

        if (summary is null)
        {
            result.Messages.Add("Assignment summary could not be loaded.");
            result.FailedSubmissionCount++;
            return result;
        }

        var candidateIds = summary.Rows
            .Where(r => r.IsReadyForFreeze && r.EffectiveSubmission is not null)
            .Select(r => r.EffectiveSubmission!.Id)
            .Distinct()
            .ToList();

        result.SelectedSubmissionCount = candidateIds.Count;

        if (candidateIds.Count == 0)
        {
            assignment.IsFrozen = true;
            await _db.SaveChangesAsync();

            result.Messages.Add("No ready submissions were found. Assignment was marked as frozen.");
            return result;
        }

        var allSubmissions = await _db.Submissions
            .Where(s => s.AssignmentId == assignmentId)
            .ToListAsync();

        foreach (var submission in allSubmissions)
        {
            submission.IsSelectedForFreeze = false;
            submission.FreezeStatus = "NotSelected";
            submission.FreezeMessage = null;
        }

        await _db.SaveChangesAsync();

        foreach (var submissionId in candidateIds)
        {
            var submission = await _db.Submissions
                .Include(s => s.Student)
                .Include(s => s.Assignment)
                .Include(s => s.Items)
                    .ThenInclude(i => i.Events)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission is null)
            {
                result.FailedSubmissionCount++;
                result.Messages.Add($"Submission {submissionId} was not found.");
                continue;
            }

            var submissionFreezeSucceeded = true;
            var itemMessages = new List<string>();

            submission.IsSelectedForFreeze = true;
            submission.FreezeStatus = "Freezing";
            submission.FrozenAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            foreach (var item in submission.Items)
            {
                var itemResult = await FreezeSubmissionItemAsync(assignment, submission, item);

                if (!itemResult.Success)
                {
                    submissionFreezeSucceeded = false;
                }

                itemMessages.Add(itemResult.Message);
            }

            submission.FrozenAt = DateTime.UtcNow;
            submission.FreezeStatus = submissionFreezeSucceeded ? "Frozen" : "FreezeFailed";
            submission.FreezeMessage = string.Join(" | ", itemMessages);

            if (submissionFreezeSucceeded)
            {
                result.FrozenSubmissionCount++;
            }
            else
            {
                result.FailedSubmissionCount++;
            }

            await _db.SaveChangesAsync();
        }

        assignment.IsFrozen = true;
        await _db.SaveChangesAsync();

        result.Messages.Add(
            $"Freeze completed. Frozen={result.FrozenSubmissionCount}, Failed={result.FailedSubmissionCount}."
        );

        return result;
    }

    private async Task<(bool Success, string Message)> FreezeSubmissionItemAsync(
        Assignment assignment,
        Submission submission,
        SubmissionItem item)
    {
        var frozenAt = DateTime.UtcNow;

        var root = Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "assignments",
            $"assignment_{assignment.Id}",
            "frozen",
            SanitizePathSegment(submission.Student.StudentNo),
            $"submission_{submission.Id}",
            $"item_{item.Id}"
        );

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);

        var repoDir = Path.Combine(root, "repo");
        var submittedDir = Path.Combine(root, "submitted");
        var snapshotZipPath = Path.Combine(root, "snapshot.zip");

        var repoUrl = $"https://github.com/{item.Owner}/{item.Repo}.git";

        try
        {
            var cloneArgs = new List<string>
            {
                "clone",
                "--depth",
                "1"
            };

            if (!string.IsNullOrWhiteSpace(item.Branch))
            {
                cloneArgs.Add("--branch");
                cloneArgs.Add(item.Branch);
            }

            cloneArgs.Add(repoUrl);
            cloneArgs.Add(repoDir);

            var cloneResult = await _commandRunner.RunAsync(
                "git",
                cloneArgs,
                timeoutSeconds: 90
            );

            if (cloneResult.TimedOut)
            {
                return await MarkItemFreezeFailedAsync(
                    item,
                    "FreezeFailed",
                    "Git clone timed out during freeze.",
                    frozenAt
                );
            }

            if (cloneResult.ExitCode != 0)
            {
                return await MarkItemFreezeFailedAsync(
                    item,
                    "FreezeFailed",
                    TrimForDisplay(cloneResult.StandardError),
                    frozenAt
                );
            }

            var shaResult = await _commandRunner.RunAsync(
                "git",
                new[] { "rev-parse", "HEAD" },
                workingDirectory: repoDir,
                timeoutSeconds: 10
            );

            var finalSha = shaResult.ExitCode == 0
                ? shaResult.StandardOutput.Trim()
                : null;

            var targetPath = string.IsNullOrWhiteSpace(item.Path)
                ? repoDir
                : Path.Combine(
                    repoDir,
                    item.Path.Replace('/', Path.DirectorySeparatorChar)
                );

            if (item.UrlKind is "Repository" or "Folder")
            {
                if (!Directory.Exists(targetPath))
                {
                    return await MarkItemFreezeFailedAsync(
                        item,
                        "FreezeFailed",
                        $"Submitted folder path was not found during freeze: {DisplayPath(item.Path)}",
                        frozenAt
                    );
                }

                CopyDirectory(targetPath, submittedDir);
            }
            else if (item.UrlKind is "File" or "RawFile")
            {
                if (!File.Exists(targetPath))
                {
                    return await MarkItemFreezeFailedAsync(
                        item,
                        "FreezeFailed",
                        $"Submitted file path was not found during freeze: {DisplayPath(item.Path)}",
                        frozenAt
                    );
                }

                Directory.CreateDirectory(submittedDir);

                var fileName = Path.GetFileName(targetPath);
                File.Copy(targetPath, Path.Combine(submittedDir, fileName), overwrite: true);
            }
            else
            {
                return await MarkItemFreezeFailedAsync(
                    item,
                    "FreezeFailed",
                    $"Unsupported URL kind during freeze: {item.UrlKind}",
                    frozenAt
                );
            }

            if (File.Exists(snapshotZipPath))
            {
                File.Delete(snapshotZipPath);
            }

            ZipFile.CreateFromDirectory(submittedDir, snapshotZipPath);

            item.FinalSha = finalSha;
            item.SnapshotPath = snapshotZipPath;
            item.FrozenAt = frozenAt;

            item.Events.Add(new SubmissionEvent
            {
                EventType = "FreezeSucceeded",
                Message = $"Submission item was frozen successfully. Final SHA={finalSha ?? "-"}",
                CreatedAt = frozenAt
            });

            await _db.SaveChangesAsync();

            return (true, $"Item {item.Id} frozen.");
        }
        catch (Exception ex)
        {
            return await MarkItemFreezeFailedAsync(
                item,
                "FreezeFailed",
                ex.Message,
                frozenAt
            );
        }
    }

    private async Task<(bool Success, string Message)> MarkItemFreezeFailedAsync(
        SubmissionItem item,
        string eventType,
        string message,
        DateTime frozenAt)
    {
        item.FrozenAt = frozenAt;

        item.Events.Add(new SubmissionEvent
        {
            EventType = eventType,
            Message = message,
            CreatedAt = frozenAt
        });

        await _db.SaveChangesAsync();

        return (false, $"Item {item.Id} failed: {message}");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destinationFile = Path.Combine(destinationDir, fileName);
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var directoryName = Path.GetFileName(directory);
            var destinationSubDir = Path.Combine(destinationDir, directoryName);
            CopyDirectory(directory, destinationSubDir);
        }
    }

    private static string DisplayPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "/" : path;
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
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