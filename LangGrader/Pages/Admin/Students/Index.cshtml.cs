using LangGrader.Data;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LangGrader.Pages.Admin.Students;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Student> _passwordHasher;

    public IndexModel(
        AppDbContext db,
        IPasswordHasher<Student> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public List<Student> Students { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Students = await _db.Students
            .OrderBy(s => s.Role)
            .ThenBy(s => s.StudentNo)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(long id)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
        {
            return NotFound();
        }

        if (student.Role == "Admin")
        {
            ErrorMessage = "Admin accounts cannot be deactivated from this page.";
            return RedirectToPage();
        }

        student.IsActive = !student.IsActive;
        await _db.SaveChangesAsync();

        Message = student.IsActive
            ? $"Student '{student.StudentNo}' was activated."
            : $"Student '{student.StudentNo}' was deactivated.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(long id)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == id);

        if (student is null)
        {
            return NotFound();
        }

        if (student.Role == "Admin")
        {
            ErrorMessage = "Admin passwords cannot be reset from this page.";
            return RedirectToPage();
        }

        var newPassword = PasswordGenerator.Generate();

        student.PasswordHash = _passwordHasher.HashPassword(student, newPassword);
        student.MustChangePassword = true;

        await _db.SaveChangesAsync();

        var csv = new StringBuilder();
        csv.AppendLine("StudentNo,Name,NewPassword");
        csv.AppendLine(string.Join(",",
            SimpleCsv.Escape(student.StudentNo),
            SimpleCsv.Escape(student.Name),
            SimpleCsv.Escape(newPassword)));

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();

        var fileName = $"reset_password_{student.StudentNo}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}