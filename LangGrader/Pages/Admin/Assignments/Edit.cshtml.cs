using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Admin.Assignments;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public AssignmentFormInput Input { get; set; } = new();

    public Assignment? Assignment { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        Assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (Assignment is null)
        {
            return NotFound();
        }

        Input = AssignmentFormSupport.FromAssignment(Assignment);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id)
    {
        Assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (Assignment is null)
        {
            return NotFound();
        }

        if (Assignment.IsFrozen)
        {
            ModelState.AddModelError(
                string.Empty,
                "Frozen assignments cannot be edited. Unfreeze the assignment first."
            );

            return Page();
        }

        if (!AssignmentFormSupport.TryApplyToAssignment(
                Assignment,
                Input,
                ModelState))
        {
            return Page();
        }

        await _db.SaveChangesAsync();

        TempData["Message"] = $"Assignment '{Assignment.Title}' was updated.";

        return RedirectToPage("/Admin/Assignments/Index");
    }
}