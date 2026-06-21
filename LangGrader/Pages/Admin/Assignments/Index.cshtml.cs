using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Admin.Assignments;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Assignment> Assignments { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Assignments = await _db.Assignments
            .OrderByDescending(a => a.Id)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostTogglePublishedAsync(long id)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (assignment is null)
        {
            return NotFound();
        }

        assignment.IsPublished = !assignment.IsPublished;

        await _db.SaveChangesAsync();

        Message = assignment.IsPublished
            ? $"Assignment '{assignment.Title}' was published."
            : $"Assignment '{assignment.Title}' was unpublished.";

        return RedirectToPage();
    }
}