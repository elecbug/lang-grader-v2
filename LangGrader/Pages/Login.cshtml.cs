using LangGrader.Data;
using LangGrader.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LangGrader.Pages;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Student> _passwordHasher;

    public LoginModel(AppDbContext db, IPasswordHasher<Student> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.StudentNo) ||
            string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = "Please enter your ID and password.";
            return Page();
        }

        var studentNo = Input.StudentNo.Trim();

        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.StudentNo == studentNo && s.IsActive);

        if (student is null)
        {
            ErrorMessage = "Invalid ID or password.";
            return Page();
        }

        var result = _passwordHasher.VerifyHashedPassword(
            student,
            student.PasswordHash,
            Input.Password
        );

        if (result == PasswordVerificationResult.Failed)
        {
            ErrorMessage = "Invalid ID or password.";
            return Page();
        }

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

        if (student.MustChangePassword && student.Role == "Student")
        {
            return RedirectToPage("/ChangePassword", new { required = true });
        }

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("/Assignments/Index");
    }

    public class LoginInput
    {
        public string StudentNo { get; set; } = "";
        public string Password { get; set; } = "";
    }
}