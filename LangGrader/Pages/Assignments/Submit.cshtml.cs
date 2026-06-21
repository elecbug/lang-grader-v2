using LangGrader.Data;
using LangGrader.Helpers;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LangGrader.Pages.Assignments;

public class SubmitModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGitHubUrlParser _urlParser;
    private readonly IRepositoryValidator _repositoryValidator;

    public SubmitModel(
        AppDbContext db,
        IGitHubUrlParser urlParser,
        IRepositoryValidator repositoryValidator)
    {
        _db = db;
        _urlParser = urlParser;
        _repositoryValidator = repositoryValidator;
    }

    public string AssignmentTitle { get; set; } = "";
    public string AssignmentDescription { get; set; } = "";
    public string DeadlineText { get; set; } = "";

    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    public long AssignmentId { get; set; }
    public bool IsAssignmentFrozen { get; set; }

    [BindProperty]
    public SubmitInput Input { get; set; } = new();

    public List<Submission> PreviousSubmissions { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id && a.IsPublished);

        if (assignment is null)
        {
            return NotFound();
        }

        await LoadPageDataAsync(assignment);

        Input.Items.Add(new SubmitItemInput
        {
            MainFilePath = "main.c"
        });

        await LoadPreviousSubmissionsAsync(id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id && a.IsPublished);

        if (assignment is null)
        {
            return NotFound();
        }

        await LoadPageDataAsync(assignment);

        var studentIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(studentIdText, out var studentId))
        {
            return RedirectToPage("/Login");
        }

        var now = DateTime.UtcNow;

        if (assignment.IsFrozen)
        {
            ErrorMessage = "This assignment has already been finalized.";
            await LoadPreviousSubmissionsAsync(id);
            return Page();
        }

        var validItems = Input.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .Select(i => new SubmitItemInput
            {
                Url = i.Url.Trim(),
                MainFilePath = string.IsNullOrWhiteSpace(i.MainFilePath)
                    ? "main.c"
                    : i.MainFilePath.Trim()
            })
            .ToList();

        if (validItems.Count == 0)
        {
            ErrorMessage = "Please enter at least one GitHub URL.";

            if (Input.Items.Count == 0)
            {
                Input.Items.Add(new SubmitItemInput { MainFilePath = "main.c" });
            }

            await LoadPreviousSubmissionsAsync(id);
            return Page();
        }

        var parsedItems = new List<(SubmitItemInput Input, GitHubUrlParseResult Parsed)>();

        foreach (var item in validItems)
        {
            var parsed = _urlParser.Parse(item.Url);

            if (!parsed.IsValid)
            {
                ErrorMessage = $"Invalid GitHub URL: {item.Url} ({parsed.ErrorMessage})";

                Input.Items = validItems;
                await LoadPreviousSubmissionsAsync(id);
                return Page();
            }

            parsedItems.Add((item, parsed));
        }

        var submission = new Submission
        {
            AssignmentId = assignment.Id,
            StudentId = studentId,
            SubmittedAt = now,
            Status = "PendingValidation",
            IsLate = now > assignment.DeadlineAt
        };

        foreach (var pair in parsedItems)
        {
            var item = pair.Input;
            var parsed = pair.Parsed;

            submission.Items.Add(new SubmissionItem
            {
                OriginalUrl = parsed.OriginalUrl,
                Owner = parsed.Owner,
                Repo = parsed.Repo,
                Branch = parsed.Branch,
                Path = parsed.Path,
                UrlKind = parsed.UrlKind,
                MainFilePath = item.MainFilePath,
                Status = "Parsed"
            });
        }

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync();

        foreach (var item in submission.Items)
        {
            var branchText = string.IsNullOrWhiteSpace(item.Branch)
                ? "(default)"
                : item.Branch;

            item.Events.Add(new SubmissionEvent
            {
                EventType = "UrlParsed",
                Message = $"URL parsed as {item.UrlKind}: {item.Owner}/{item.Repo}, branch={branchText}, path={(string.IsNullOrWhiteSpace(item.Path) ? "/" : item.Path)}",
                CreatedAt = DateTime.UtcNow
            });

            item.Status = "Validating";

            await _db.SaveChangesAsync();

            var validationResult = await _repositoryValidator.ValidateAsync(item, assignment);

            item.Status = validationResult.Status;
            item.ObservedSha = validationResult.ObservedSha;
            item.ValidatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(validationResult.ResolvedBranch))
            {
                item.Branch = validationResult.ResolvedBranch;
            }

            item.Events.Add(new SubmissionEvent
            {
                EventType = validationResult.IsValid ? "ValidationSucceeded" : "ValidationFailed",
                Message = validationResult.Message,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        submission.Status = submission.Items.All(i => i.Status == "Valid")
            ? "Valid"
            : "ValidationFailed";

        await _db.SaveChangesAsync();

        Message = "Submission URLs have been saved and validated.";

        Input = new SubmitInput();
        Input.Items.Add(new SubmitItemInput
        {
            MainFilePath = "main.c"
        });

        await LoadPreviousSubmissionsAsync(id);

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, long submissionId)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id && a.IsPublished);

        if (assignment is null)
        {
            return NotFound();
        }

        var studentIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(studentIdText, out var studentId))
        {
            return RedirectToPage("/Login");
        }

        var submission = await _db.Submissions
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s =>
                s.Id == submissionId &&
                s.AssignmentId == assignment.Id &&
                s.StudentId == studentId &&
                !s.IsDeleted);

        if (submission is null)
        {
            return NotFound();
        }

        if (assignment.IsFrozen)
        {
            ErrorMessage = "This assignment has already been frozen.";
            await LoadPageDataAsync(assignment);

            Input.Items.Add(new SubmitItemInput
            {
                MainFilePath = "main.c"
            });

            await LoadPreviousSubmissionsAsync(id);
            return Page();
        }

        submission.IsDeleted = true;
        submission.DeletedAt = DateTime.UtcNow;
        submission.Status = "Deleted";

        foreach (var item in submission.Items)
        {
            item.Status = "Deleted";
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Assignments/Submit", new { id });
    }

    private async Task LoadPageDataAsync(Assignment assignment)
    {
        AssignmentId = assignment.Id;
        AssignmentTitle = assignment.Title;
        AssignmentDescription = assignment.Description;
        DeadlineText = TimeViewHelper.FormatKstMinute(assignment.DeadlineAt);
        IsAssignmentFrozen = assignment.IsFrozen;
    }

    private async Task LoadPreviousSubmissionsAsync(long assignmentId)
    {
        var studentIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(studentIdText, out var studentId))
        {
            PreviousSubmissions = new List<Submission>();
            return;
        }

        PreviousSubmissions = await _db.Submissions
            .Include(s => s.Items)
            .Where(s =>
                s.AssignmentId == assignmentId &&
                s.StudentId == studentId &&
                !s.IsDeleted)
            .OrderByDescending(s => s.SubmittedAt)
            .Take(10)
            .ToListAsync();
    }

    public class SubmitInput
    {
        public List<SubmitItemInput> Items { get; set; } = new();
    }

    public class SubmitItemInput
    {
        public string Url { get; set; } = "";
        public string MainFilePath { get; set; } = "main.c";
    }
}