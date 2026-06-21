using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LangGrader.Pages.Admin.Assignments;

[Authorize(Roles = "Admin")]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;

    public CreateModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public AssignmentFormInput Input { get; set; } = new();

    public void OnGet()
    {
        Input = AssignmentFormSupport.CreateDefaultInput();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var assignment = new Assignment();

        if (!AssignmentFormSupport.TryApplyToAssignment(
                assignment,
                Input,
                ModelState))
        {
            return Page();
        }

        _db.Assignments.Add(assignment);
        await _db.SaveChangesAsync();

        TempData["Message"] = $"Assignment '{assignment.Title}' was created.";

        return RedirectToPage("/Admin/Assignments/Index");
    }
}