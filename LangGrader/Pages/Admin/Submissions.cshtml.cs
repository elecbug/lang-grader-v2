using LangGrader.Data;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace LangGrader.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SubmissionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IEffectiveSubmissionSelector _effectiveSubmissionSelector;
    private readonly IAssignmentFreezeService _assignmentFreezeService; 
    private readonly IWebHostEnvironment _environment;

    public Dictionary<long, SnapshotFileListModel> SnapshotFiles { get; set; } = new();

    public SubmissionsModel(
        AppDbContext db,
        IEffectiveSubmissionSelector effectiveSubmissionSelector,
        IAssignmentFreezeService assignmentFreezeService,
        IWebHostEnvironment environment)
    {
        _db = db;
        _effectiveSubmissionSelector = effectiveSubmissionSelector;
        _assignmentFreezeService = assignmentFreezeService;
        _environment = environment;
    }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? AssignmentId { get; set; }

    public List<Assignment> Assignments { get; set; } = new();

    public AssignmentSubmissionSummary? Summary { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Assignments = await _db.Assignments
            .OrderByDescending(a => a.Id)
            .ToListAsync();

        if (Assignments.Count == 0)
        {
            return Page();
        }

        AssignmentId ??= Assignments.First().Id;

        Summary = await _effectiveSubmissionSelector
            .GetAssignmentSummaryAsync(AssignmentId.Value);

        if (Summary is null)
        {
            return NotFound();
        }

        LoadSnapshotFileLists();

        return Page();
    }

    public async Task<IActionResult> OnPostFreezeAsync(long assignmentId)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null)
        {
            return NotFound();
        }

        if (assignment.IsFrozen)
        {
            ErrorMessage = "This assignment is already frozen.";
            return RedirectToPage("/Admin/Submissions", new { assignmentId });
        }

        var result = await _assignmentFreezeService.FreezeAssignmentAsync(assignmentId);

        if (result.HasFailures)
        {
            ErrorMessage = string.Join(" ", result.Messages);
        }
        else
        {
            Message = string.Join(" ", result.Messages);
        }

        return RedirectToPage("/Admin/Submissions", new { assignmentId });
    }

    public async Task<IActionResult> OnPostUnfreezeAsync(long assignmentId)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null)
        {
            return NotFound();
        }

        if (!assignment.IsFrozen)
        {
            ErrorMessage = "This assignment is not frozen.";
            return RedirectToPage("/Admin/Submissions", new { assignmentId });
        }

        var result = await _assignmentFreezeService.UnfreezeAssignmentAsync(assignmentId);

        if (result.HasFailures)
        {
            ErrorMessage = string.Join(" ", result.Messages);
        }
        else
        {
            Message = string.Join(" ", result.Messages);
        }

        return RedirectToPage("/Admin/Submissions", new { assignmentId });
    }

    public async Task<IActionResult> OnGetDownloadSnapshotAsync(long itemId)
    {
        var item = await _db.SubmissionItems
            .Include(i => i.Submission)
                .ThenInclude(s => s.Student)
            .Include(i => i.Submission)
                .ThenInclude(s => s.Assignment)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(item.SnapshotPath))
        {
            return NotFound("Snapshot path is empty.");
        }

        if (!TryGetSafeSnapshotZipPath(item, out var fullPath, out var error))
        {
            return BadRequest(error);
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("Snapshot file does not exist on disk.");
        }

        var assignmentTitle = SanitizeFileName(item.Submission.Assignment.Title);
        var studentNo = SanitizeFileName(item.Submission.Student.StudentNo);
        var repoName = SanitizeFileName($"{item.Owner}_{item.Repo}");

        var fileName = $"{assignmentTitle}_{studentNo}_{repoName}_snapshot.zip";

        return PhysicalFile(
            fullPath,
            "application/zip",
            fileName
        );
    }

    public sealed class SnapshotFileListModel
    {
        public bool Exists { get; init; }

        public bool IsTruncated { get; init; }

        public string? ErrorMessage { get; init; }

        public List<string> Entries { get; init; } = new();
    }

    private void LoadSnapshotFileLists()
    {
        SnapshotFiles.Clear();

        if (Summary is null)
        {
            return;
        }

        var items = Summary.Rows
            .Where(r => r.EffectiveSubmission is not null)
            .SelectMany(r => r.EffectiveSubmission!.Items)
            .Where(i => !string.IsNullOrWhiteSpace(i.SnapshotPath))
            .ToList();

        foreach (var item in items)
        {
            SnapshotFiles[item.Id] = BuildSnapshotFileList(item);
        }
    }

    private SnapshotFileListModel BuildSnapshotFileList(SubmissionItem item)
    {
        if (!TryGetSafeSnapshotZipPath(item, out var fullPath, out var error))
        {
            return new SnapshotFileListModel
            {
                Exists = false,
                ErrorMessage = error
            };
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return new SnapshotFileListModel
            {
                Exists = false,
                ErrorMessage = "Snapshot file does not exist on disk."
            };
        }

        const int limit = 80;

        try
        {
            using var archive = ZipFile.OpenRead(fullPath);

            var entries = archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Select(e => e.FullName.Replace('\\', '/'))
                .OrderBy(e => e)
                .Take(limit + 1)
                .ToList();

            var isTruncated = entries.Count > limit;

            if (isTruncated)
            {
                entries = entries.Take(limit).ToList();
            }

            return new SnapshotFileListModel
            {
                Exists = true,
                IsTruncated = isTruncated,
                Entries = entries
            };
        }
        catch (Exception ex)
        {
            return new SnapshotFileListModel
            {
                Exists = false,
                ErrorMessage = $"Could not read snapshot ZIP: {ex.Message}"
            };
        }
    }

    private bool TryGetSafeSnapshotZipPath(
        SubmissionItem item,
        out string fullPath,
        out string error)
    {
        fullPath = "";
        error = "";

        if (string.IsNullOrWhiteSpace(item.SnapshotPath))
        {
            error = "Snapshot path is empty.";
            return false;
        }

        var candidatePath = Path.IsPathRooted(item.SnapshotPath)
            ? item.SnapshotPath
            : Path.Combine(_environment.ContentRootPath, item.SnapshotPath);

        fullPath = Path.GetFullPath(candidatePath);

        var storageRoot = Path.GetFullPath(
            Path.Combine(_environment.ContentRootPath, "storage")
        );

        if (!IsUnderDirectory(fullPath, storageRoot))
        {
            error = "Snapshot path is outside the storage directory.";
            return false;
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "Snapshot path is not a ZIP file.";
            return false;
        }

        return true;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directory)
        ) + Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(path);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return fullPath.StartsWith(fullDirectory, comparison);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "snapshot";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        value = value.Trim();

        return value.Length <= 80
            ? value
            : value[..80];
    }
}