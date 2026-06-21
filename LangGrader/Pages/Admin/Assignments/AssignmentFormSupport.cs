using System.Globalization;
using System.Text.Json;
using LangGrader.Helpers;
using LangGrader.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LangGrader.Pages.Admin.Assignments;

public static class AssignmentFormSupport
{
    private const string DateTimeLocalFormat = "yyyy-MM-ddTHH:mm";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static AssignmentFormInput FromAssignment(Assignment assignment)
    {
        return new AssignmentFormInput
        {
            Title = assignment.Title,
            Description = assignment.Description,
            OpenAtKst = ToDateTimeLocalInput(assignment.OpenAt),
            DeadlineAtKst = ToDateTimeLocalInput(assignment.DeadlineAt),
            IsPublished = assignment.IsPublished,
            RequiredFilesJson = string.IsNullOrWhiteSpace(assignment.RequiredFilesJson)
                ? "[]"
                : assignment.RequiredFilesJson,
            MainFileCandidatesJson = string.IsNullOrWhiteSpace(assignment.MainFileCandidatesJson)
                ? "[\"main.c\"]"
                : assignment.MainFileCandidatesJson
        };
    }

    public static AssignmentFormInput CreateDefaultInput()
    {
        var nowKst = TimeViewHelper.ToKst(DateTime.UtcNow);
        var openAt = new DateTime(
            nowKst.Year,
            nowKst.Month,
            nowKst.Day,
            nowKst.Hour,
            nowKst.Minute,
            0
        );

        var deadline = openAt.AddDays(7);

        return new AssignmentFormInput
        {
            Title = "",
            Description = "",
            OpenAtKst = openAt.ToString(DateTimeLocalFormat),
            DeadlineAtKst = deadline.ToString(DateTimeLocalFormat),
            IsPublished = false,
            RequiredFilesJson = "[]",
            MainFileCandidatesJson = "[\"main.c\"]"
        };
    }

    public static bool TryApplyToAssignment(
        Assignment assignment,
        AssignmentFormInput input,
        ModelStateDictionary modelState)
    {
        if (!TryParseKstDateTime(input.OpenAtKst, out var openAtUtc))
        {
            modelState.AddModelError(
                nameof(input.OpenAtKst),
                "Open time must be a valid KST date and time."
            );
        }

        if (!TryParseKstDateTime(input.DeadlineAtKst, out var deadlineAtUtc))
        {
            modelState.AddModelError(
                nameof(input.DeadlineAtKst),
                "Deadline must be a valid KST date and time."
            );
        }

        if (modelState.IsValid && deadlineAtUtc <= openAtUtc)
        {
            modelState.AddModelError(
                nameof(input.DeadlineAtKst),
                "Deadline must be later than open time."
            );
        }

        if (!TryNormalizeStringArrayJson(
                input.RequiredFilesJson,
                allowEmpty: true,
                out var normalizedRequiredFilesJson,
                out var requiredFilesError))
        {
            modelState.AddModelError(
                nameof(input.RequiredFilesJson),
                requiredFilesError
            );
        }

        if (!TryNormalizeStringArrayJson(
                input.MainFileCandidatesJson,
                allowEmpty: false,
                out var normalizedMainFileCandidatesJson,
                out var mainFileCandidatesError))
        {
            modelState.AddModelError(
                nameof(input.MainFileCandidatesJson),
                mainFileCandidatesError
            );
        }

        if (!modelState.IsValid)
        {
            return false;
        }

        assignment.Title = input.Title.Trim();
        assignment.Description = input.Description?.Trim() ?? "";
        assignment.OpenAt = openAtUtc;
        assignment.DeadlineAt = deadlineAtUtc;
        assignment.IsPublished = input.IsPublished;
        assignment.RequiredFilesJson = normalizedRequiredFilesJson;
        assignment.MainFileCandidatesJson = normalizedMainFileCandidatesJson;

        return true;
    }

    private static string ToDateTimeLocalInput(DateTime utcDateTime)
    {
        return TimeViewHelper
            .ToKst(utcDateTime)
            .ToString(DateTimeLocalFormat);
    }

    private static bool TryParseKstDateTime(string value, out DateTime utcDateTime)
    {
        utcDateTime = default;

        if (!DateTime.TryParseExact(
                value,
                DateTimeLocalFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var kstDateTime))
        {
            return false;
        }

        var unspecifiedKst = DateTime.SpecifyKind(kstDateTime, DateTimeKind.Unspecified);
        utcDateTime = unspecifiedKst.AddHours(-9);

        return true;
    }

    private static bool TryNormalizeStringArrayJson(
        string json,
        bool allowEmpty,
        out string normalizedJson,
        out string error)
    {
        normalizedJson = "[]";
        error = "";

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json);

            if (values is null)
            {
                error = "JSON value must be an array of strings.";
                return false;
            }

            var normalizedValues = values
                .Select(v => v?.Trim() ?? "")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!allowEmpty && normalizedValues.Count == 0)
            {
                error = "At least one file candidate is required.";
                return false;
            }

            normalizedJson = JsonSerializer.Serialize(normalizedValues, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            error = "JSON value must be a valid array of strings. Example: [\"main.c\"]";
            return false;
        }
    }
}