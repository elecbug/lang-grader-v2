using LangGrader.Data;
using LangGrader.Helpers;
using LangGrader.Models;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

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

        var frozenAtUtc = DateTime.UtcNow;
        var freezeRoot = CreateFrozenAssignmentRoot(assignment, frozenAtUtc);

        assignment.FrozenAt = frozenAtUtc;
        assignment.FreezeRootPath = freezeRoot;

        SafeDeleteDirectory(freezeRoot);
        Directory.CreateDirectory(freezeRoot);

        await _db.SaveChangesAsync();

        if (candidateIds.Count == 0)
        {
            assignment.IsFrozen = true;
            assignment.FrozenAt = frozenAtUtc;
            assignment.FreezeRootPath = freezeRoot;

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
            submission.FrozenAt = null;
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

            for (var i = 0; i < submission.Items.Count; i++)
            {
                var item = submission.Items[i];
                var itemNumber = i + 1;

                var itemResult = await FreezeSubmissionItemAsync(
                    assignment,
                    submission,
                    item,
                    itemNumber
                );

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
        assignment.FrozenAt = frozenAtUtc;
        assignment.FreezeRootPath = freezeRoot;

        await _db.SaveChangesAsync();

        result.Messages.Add(
            $"Freeze completed. Frozen={result.FrozenSubmissionCount}, Failed={result.FailedSubmissionCount}."
        );

        return result;
    }

    public async Task<AssignmentFreezeResult> UnfreezeAssignmentAsync(long assignmentId)
    {
        var result = new AssignmentFreezeResult
        {
            AssignmentId = assignmentId
        };

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null)
        {
            result.FailedSubmissionCount++;
            result.Messages.Add("Assignment not found.");
            return result;
        }

        var submissions = await _db.Submissions
            .Include(s => s.Items)
                .ThenInclude(i => i.Events)
            .Where(s => s.AssignmentId == assignmentId)
            .ToListAsync();

        var now = DateTime.UtcNow;

        foreach (var submission in submissions)
        {
            var hadFreezeInfo =
                submission.IsSelectedForFreeze ||
                submission.FrozenAt.HasValue ||
                submission.FreezeStatus != "NotFrozen" ||
                submission.Items.Any(i =>
                    i.FrozenAt.HasValue ||
                    !string.IsNullOrWhiteSpace(i.FinalSha) ||
                    !string.IsNullOrWhiteSpace(i.SnapshotPath));

            submission.IsSelectedForFreeze = false;
            submission.FrozenAt = null;
            submission.FreezeStatus = "NotFrozen";
            submission.FreezeMessage = null;

            foreach (var item in submission.Items)
            {
                if (hadFreezeInfo)
                {
                    item.Events.Add(new SubmissionEvent
                    {
                        EventType = "FreezeCleared",
                        Message = "Freeze metadata was cleared by an administrator.",
                        CreatedAt = now
                    });
                }

                item.FinalSha = null;
                item.SnapshotPath = null;
                item.FrozenAt = null;
            }
        }

        assignment.IsFrozen = false;
        assignment.FrozenAt = null;
        assignment.FreezeRootPath = null;

        DeleteFrozenAssignmentRoot(assignment);
        DeleteLegacyFrozenDirectory(assignment);

        await _db.SaveChangesAsync();

        result.Messages.Add("Assignment was unfrozen. Freeze metadata and frozen snapshots were cleared.");
        return result;
    }

    private async Task<(bool Success, string Message)> FreezeSubmissionItemAsync(
        Assignment assignment,
        Submission submission,
        SubmissionItem item,
        int itemNumber)
    {
        var frozenAt = DateTime.UtcNow;

        var root = BuildFreezeItemRoot(
            assignment,
            submission,
            item,
            itemNumber
        );

        if (Directory.Exists(root))
        {
            SafeDeleteDirectory(root);
        }

        Directory.CreateDirectory(root);

        var tempRoot = Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "temp",
            "freeze",
            $"assignment_{assignment.Id}",
            $"submission_{submission.Id}",
            $"item_{item.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}"
        );

        SafeDeleteDirectory(tempRoot);
        Directory.CreateDirectory(tempRoot);

        var repoDir = Path.Combine(tempRoot, "repo");
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
        finally
        {
            SafeDeleteDirectory(tempRoot);
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

    private string BuildFreezeItemRoot(
        Assignment assignment,
        Submission submission,
        SubmissionItem item,
        int itemNumber)
    {
        var assignmentRoot = GetFrozenAssignmentRoot(assignment);
        var studentSegment = SanitizePathSegment(submission.Student.StudentNo);

        var repoSegment = SanitizePathSegment($"{item.Owner}_{item.Repo}");
        var itemSegment = $"{itemNumber:00}_{repoSegment}";

        return Path.Combine(
            assignmentRoot,
            studentSegment,
            itemSegment
        );
    }

    private string GetCurrentFrozenAssignmentRoot(Assignment assignment)
    {
        return Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "frozen",
            $"assignment_{assignment.Id}"
        );
    }

    private string GetLegacyFrozenAssignmentRoot(Assignment assignment)
    {
        return Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "assignments",
            $"assignment_{assignment.Id}",
            "frozen"
        );
    }

    private void DeleteLegacyFrozenDirectory(Assignment assignment)
    {
        var root = GetLegacyFrozenAssignmentRoot(assignment);

        SafeDeleteDirectory(root);
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

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(200 * attempt);
            }
        }

        ClearReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
                // Ignore attribute cleanup failures.
            }
        }

        foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(directory, FileAttributes.Normal);
            }
            catch
            {
                // Ignore attribute cleanup failures.
            }
        }

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        catch
        {
            // Ignore attribute cleanup failures.
        }
    }

    private string CreateFrozenAssignmentRoot(Assignment assignment, DateTime frozenAtUtc)
    {
        var kst = TimeViewHelper.ToKst(frozenAtUtc);
        var timestamp = kst.ToString("yyyy-MM-dd-HH-mm-ss");

        var assignmentSegment = SanitizePathSegment(assignment.Title);

        if (string.IsNullOrWhiteSpace(assignmentSegment))
        {
            assignmentSegment = $"assignment_{assignment.Id}";
        }

        var folderName = $"{timestamp}_{assignmentSegment}";

        return Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "frozen",
            folderName
        );
    }

    private string GetFrozenAssignmentRoot(Assignment assignment)
    {
        if (!string.IsNullOrWhiteSpace(assignment.FreezeRootPath))
        {
            return assignment.FreezeRootPath;
        }

        return Path.Combine(
            _environment.ContentRootPath,
            "storage",
            "frozen",
            $"assignment_{assignment.Id}"
        );
    }

    private void DeleteFrozenAssignmentRoot(Assignment assignment)
    {
        var root = GetFrozenAssignmentRoot(assignment);

        SafeDeleteDirectory(root);
    }
}