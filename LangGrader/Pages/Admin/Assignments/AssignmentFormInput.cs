using System.ComponentModel.DataAnnotations;

namespace LangGrader.Pages.Admin.Assignments;

public sealed class AssignmentFormInput
{
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    [Required]
    public string OpenAtKst { get; set; } = "";

    [Required]
    public string DeadlineAtKst { get; set; } = "";

    public bool IsPublished { get; set; }

    public string RequiredFilesJson { get; set; } = "[]";

    public string MainFileCandidatesJson { get; set; } = "[\"main.c\"]";
}