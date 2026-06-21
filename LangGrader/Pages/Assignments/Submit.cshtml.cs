using LangGrader.Data;
using LangGrader.Helpers;
using LangGrader.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LangGrader.Pages.Assignments;

public class SubmitModel : PageModel
{
    private readonly AppDbContext _db;

    public SubmitModel(AppDbContext db)
    {
        _db = db;
    }

    public string AssignmentTitle { get; set; } = "";
    public string DeadlineText { get; set; } = "";

    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

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

        AssignmentTitle = assignment.Title;
        DeadlineText = TimeViewHelper.FormatKstMinute(assignment.DeadlineAt);

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

        AssignmentTitle = assignment.Title;
        DeadlineText = TimeViewHelper.FormatKstMinute(assignment.DeadlineAt);

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

        var submission = new Submission
        {
            AssignmentId = assignment.Id,
            StudentId = studentId,
            SubmittedAt = now,
            Status = "PendingValidation",
            IsLate = now > assignment.DeadlineAt
        };

        foreach (var item in validItems)
        {
            submission.Items.Add(new SubmissionItem
            {
                OriginalUrl = item.Url,
                MainFilePath = item.MainFilePath,
                Status = "PendingValidation",
                UrlKind = "Unknown"
            });
        }

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync();

        Message = "The submission URL has been saved. URL validation will be performed in the next step.";

        Input = new SubmitInput();
        Input.Items.Add(new SubmitItemInput
        {
            MainFilePath = "main.c"
        });

        await LoadPreviousSubmissionsAsync(id);

        return Page();
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
            .Where(s => s.AssignmentId == assignmentId && s.StudentId == studentId)
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