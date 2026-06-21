using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DatabaseModel : PageModel
{
    private readonly AppDbContext _db;

    public DatabaseModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Student> Students { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
    public List<SubmissionItem> SubmissionItems { get; set; } = new();
    public List<SubmissionEvent> SubmissionEvents { get; set; } = new();

    public async Task OnGetAsync()
    {
        Students = await _db.Students
            .OrderBy(s => s.StudentNo)
            .ToListAsync();

        Assignments = await _db.Assignments
            .OrderByDescending(a => a.Id)
            .ToListAsync();

        Submissions = await _db.Submissions
            .Include(s => s.Student)
            .Include(s => s.Assignment)
            .Include(s => s.Items)
            .OrderByDescending(s => s.Id)
            .ToListAsync();

        SubmissionItems = await _db.SubmissionItems
            .Include(i => i.Submission)
                .ThenInclude(s => s.Student)
            .Include(i => i.Submission)
                .ThenInclude(s => s.Assignment)
            .OrderByDescending(i => i.Id)
            .ToListAsync();

        SubmissionEvents = await _db.SubmissionEvents
            .Include(e => e.SubmissionItem)
                .ThenInclude(i => i.Submission)
                    .ThenInclude(s => s.Student)
            .OrderByDescending(e => e.Id)
            .ToListAsync();
    }
}