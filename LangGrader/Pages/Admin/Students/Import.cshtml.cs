using System.Text;
using LangGrader.Data;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages.Admin.Students;

[Authorize(Roles = "Admin")]
public class ImportModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Student> _passwordHasher;

    public ImportModel(
        AppDbContext db,
        IPasswordHasher<Student> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public IFormFile? CsvFile { get; set; }

    [BindProperty]
    public bool UpdateExistingNames { get; set; } = true;

    [BindProperty]
    public bool ReactivateExistingStudents { get; set; } = false;

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CsvFile is null || CsvFile.Length == 0)
        {
            ErrorMessage = "Please upload a CSV file.";
            return Page();
        }

        string csvText;

        using (var reader = new StreamReader(CsvFile.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            csvText = await reader.ReadToEndAsync();
        }

        var rows = SimpleCsv.Parse(csvText);

        if (rows.Count == 0)
        {
            ErrorMessage = "The CSV file is empty.";
            return Page();
        }

        var header = rows[0]
            .Select(v => v.Trim())
            .ToArray();

        var studentNoIndex = FindColumn(header, "StudentNo", "Student No", "student_no", "studentId", "StudentId");
        var nameIndex = FindColumn(header, "Name", "StudentName", "Student Name", "name");

        if (studentNoIndex < 0 || nameIndex < 0)
        {
            ErrorMessage = "CSV header must contain StudentNo and Name columns.";
            return Page();
        }

        var existingStudents = await _db.Students
            .ToDictionaryAsync(s => s.StudentNo, StringComparer.OrdinalIgnoreCase);

        var createdRows = new List<(string StudentNo, string Name, string Password)>();

        var createdCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];

            var studentNo = GetCell(row, studentNoIndex).Trim();
            var name = GetCell(row, nameIndex).Trim();

            if (string.IsNullOrWhiteSpace(studentNo) || string.IsNullOrWhiteSpace(name))
            {
                skippedCount++;
                continue;
            }

            if (existingStudents.TryGetValue(studentNo, out var existing))
            {
                var changed = false;

                if (UpdateExistingNames && existing.Name != name)
                {
                    existing.Name = name;
                    changed = true;
                }

                if (ReactivateExistingStudents && !existing.IsActive && existing.Role == "Student")
                {
                    existing.IsActive = true;
                    changed = true;
                }

                if (changed)
                {
                    updatedCount++;
                }
                else
                {
                    skippedCount++;
                }

                continue;
            }

            var password = PasswordGenerator.Generate();

            var student = new Student
            {
                StudentNo = studentNo,
                Name = name,
                Role = "Student",
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };

            student.PasswordHash = _passwordHasher.HashPassword(student, password);

            _db.Students.Add(student);
            existingStudents[studentNo] = student;

            createdRows.Add((studentNo, name, password));
            createdCount++;
        }

        await _db.SaveChangesAsync();

        if (createdRows.Count == 0)
        {
            TempData["Message"] =
                $"Import completed. Created={createdCount}, Updated={updatedCount}, Skipped={skippedCount}. No new password file was generated.";

            return RedirectToPage("/Admin/Students/Index");
        }

        var outputCsv = new StringBuilder();
        outputCsv.AppendLine("StudentNo,Name,InitialPassword");

        foreach (var row in createdRows)
        {
            outputCsv.AppendLine(string.Join(",",
                SimpleCsv.Escape(row.StudentNo),
                SimpleCsv.Escape(row.Name),
                SimpleCsv.Escape(row.Password)));
        }

        outputCsv.AppendLine();
        outputCsv.AppendLine($"Created,{createdCount}");
        outputCsv.AppendLine($"Updated,{updatedCount}");
        outputCsv.AppendLine($"Skipped,{skippedCount}");

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(outputCsv.ToString()))
            .ToArray();

        var fileName = $"student_initial_passwords_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static int FindColumn(string[] header, params string[] candidates)
    {
        for (var i = 0; i < header.Length; i++)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(header[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string GetCell(string[] row, int index)
    {
        if (index < 0 || index >= row.Length)
        {
            return "";
        }

        return row[index];
    }
}