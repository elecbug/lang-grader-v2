using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Assignments;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Assignment> Assignments { get; set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;

        var assignments = await _db.Assignments
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.Id)
            .ToListAsync();

        Assignments = assignments
            .Where(a => a.OpenAt <= now)
            .ToList();
    }
}