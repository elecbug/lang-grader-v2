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

    public SubmissionsModel(
        AppDbContext db,
        IEffectiveSubmissionSelector effectiveSubmissionSelector)
    {
        _db = db;
        _effectiveSubmissionSelector = effectiveSubmissionSelector;
    }

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
}