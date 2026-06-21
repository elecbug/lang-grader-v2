using LangGrader.Data;
using LangGrader.Models;
using LangGrader.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=assignment_grader.db";

    options.UseSqlite(connectionString);
});

builder.Services.AddScoped<IPasswordHasher<Student>, PasswordHasher<Student>>();
builder.Services.AddScoped<IGitHubUrlParser, GitHubUrlParser>();
builder.Services.AddScoped<ICommandRunner, CommandRunner>();
builder.Services.AddScoped<IRepositoryValidator, GitRepositoryValidator>();
builder.Services.AddScoped<IEffectiveSubmissionSelector, EffectiveSubmissionSelector>();
builder.Services.AddScoped<IAssignmentFreezeService, AssignmentFreezeService>();

builder.Services.Configure<AutoFreezeOptions>(
    builder.Configuration.GetSection("AutoFreeze")
);

builder.Services.AddHostedService<AssignmentAutoFreezeHostedService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireRole("Admin");
    });
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Assignments");
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");

    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();

app.Use(async (context, next) =>
{
    var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
    var mustChangePassword = context.User.FindFirst("MustChangePassword")?.Value == "true";
    var isAdmin = context.User.IsInRole("Admin");

    if (isAuthenticated && mustChangePassword && !isAdmin)
    {
        var path = context.Request.Path;

        var allowed =
            path.StartsWithSegments("/ChangePassword") ||
            path.StartsWithSegments("/Logout") ||
            path.StartsWithSegments("/Login") ||
            path.StartsWithSegments("/css") ||
            path.StartsWithSegments("/js") ||
            path.StartsWithSegments("/lib") ||
            path.StartsWithSegments("/favicon.ico");

        if (!allowed)
        {
            context.Response.Redirect("/ChangePassword?required=true");
            return;
        }
    }

    await next();
});

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();