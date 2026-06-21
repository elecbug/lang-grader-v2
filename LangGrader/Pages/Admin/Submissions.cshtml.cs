using LangGrader.Data;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SubmissionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IEffectiveSubmissionSelector _effectiveSubmissionSelector;
    private readonly IAssignmentFreezeService _assignmentFreezeService;

    public SubmissionsModel(
        AppDbContext db,
        IEffectiveSubmissionSelector effectiveSubmissionSelector,
        IAssignmentFreezeService assignmentFreezeService)
    {
        _db = db;
        _effectiveSubmissionSelector = effectiveSubmissionSelector;
        _assignmentFreezeService = assignmentFreezeService;
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
}