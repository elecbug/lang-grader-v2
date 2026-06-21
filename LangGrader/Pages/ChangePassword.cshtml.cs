using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Pages;

[Authorize]
public class ChangePasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Student> _passwordHasher;

    public ChangePasswordModel(
        AppDbContext db,
        IPasswordHasher<Student> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool Required { get; set; }

    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var student = await GetCurrentStudentAsync();

        if (student is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToPage("/Login");
        }

        Required = Required || student.MustChangePassword;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var student = await GetCurrentStudentAsync();

        if (student is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToPage("/Login");
        }

        Required = student.MustChangePassword;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(
            student,
            student.PasswordHash,
            Input.CurrentPassword
        );

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(
                nameof(Input.CurrentPassword),
                "Current password is incorrect."
            );

            return Page();
        }

        if (Input.NewPassword != Input.ConfirmNewPassword)
        {
            ModelState.AddModelError(
                nameof(Input.ConfirmNewPassword),
                "New password and confirmation do not match."
            );

            return Page();
        }

        if (Input.NewPassword == Input.CurrentPassword)
        {
            ModelState.AddModelError(
                nameof(Input.NewPassword),
                "New password must be different from the current password."
            );

            return Page();
        }

        student.PasswordHash = _passwordHasher.HashPassword(student, Input.NewPassword);
        student.MustChangePassword = false;

        await _db.SaveChangesAsync();

        await RefreshSignInAsync(student);

        TempData["Message"] = "Password was changed successfully.";

        return RedirectToPage("/Assignments/Index");
    }

    private async Task<Student?> GetCurrentStudentAsync()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(idValue, out var studentId))
        {
            return null;
        }

        return await _db.Students
            .FirstOrDefaultAsync(s => s.Id == studentId && s.IsActive);
    }

    private async Task RefreshSignInAsync(Student student)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, student.Id.ToString()),
            new(ClaimTypes.Name, student.StudentNo),
            new("StudentNo", student.StudentNo),
            new("StudentName", student.Name),
            new("MustChangePassword", student.MustChangePassword ? "true" : "false"),
            new(ClaimTypes.Role, student.Role)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme
        );

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal
        );
    }

    public sealed class ChangePasswordInput
    {
        [Required]
        public string CurrentPassword { get; set; } = "";

        [Required]
        [MinLength(6)]
        [MaxLength(100)]
        public string NewPassword { get; set; } = "";

        [Required]
        [MinLength(6)]
        [MaxLength(100)]
        public string ConfirmNewPassword { get; set; } = "";
    }
}