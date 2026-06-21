using LangGrader.Data;
using LangGrader.Models;
using Microsoft.EntityFrameworkCore;

namespace LangGrader.Services;

public sealed class EffectiveSubmissionSelector : IEffectiveSubmissionSelector
{
    private readonly AppDbContext _db;

    public EffectiveSubmissionSelector(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AssignmentSubmissionSummary?> GetAssignmentSummaryAsync(long assignmentId)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment is null)
        {
            return null;
        }

        var students = await _db.Students
            .Where(s => s.IsActive && s.Role == "Student")
            .OrderBy(s => s.StudentNo)
            .ToListAsync();

        var submissions = await _db.Submissions
            .Include(s => s.Items)
            .Where(s => s.AssignmentId == assignmentId)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync();

        var rows = new List<SubmissionSummaryRow>();

        foreach (var student in students)
        {
            var studentSubmissions = submissions
                .Where(s => s.StudentId == student.Id)
                .OrderByDescending(s => s.SubmittedAt)
                .ToList();

            rows.Add(BuildRow(assignment, student, studentSubmissions));
        }

        return new AssignmentSubmissionSummary
        {
            Assignment = assignment,
            Rows = rows
        };
    }

    private static SubmissionSummaryRow BuildRow(
        Assignment assignment,
        Student student,
        List<Submission> submissions)
    {
        var latestSubmission = submissions.FirstOrDefault();

        var deletedSubmissionCount = submissions.Count(s => s.IsDeleted);

        var nonDeletedSubmissions = submissions
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

        var onTimeSubmissions = nonDeletedSubmissions
            .Where(s => s.SubmittedAt <= assignment.DeadlineAt)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

        var validOnTimeSubmissions = onTimeSubmissions
            .Where(IsValidSubmission)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

        var hasLateSubmission = nonDeletedSubmissions
            .Any(s => s.SubmittedAt > assignment.DeadlineAt);

        var hasValidationFailure = nonDeletedSubmissions
            .Any(s => IsValidationFailedSubmission(s));

        if (validOnTimeSubmissions.Count > 0)
        {
            var effective = validOnTimeSubmissions.First();

            return new SubmissionSummaryRow
            {
                StudentId = student.Id,
                StudentNo = student.StudentNo,
                StudentName = student.Name,
                TotalSubmissionCount = submissions.Count,
                DeletedSubmissionCount = deletedSubmissionCount,
                LatestSubmission = latestSubmission,
                EffectiveSubmission = effective,
                EffectiveStatus = "Ready",
                Reason = "Latest valid on-time submission selected.",
                IsReadyForFreeze = true,
                HasLateSubmission = hasLateSubmission,
                HasValidationFailure = hasValidationFailure
            };
        }

        if (onTimeSubmissions.Count > 0)
        {
            var effective = onTimeSubmissions.First();

            return new SubmissionSummaryRow
            {
                StudentId = student.Id,
                StudentNo = student.StudentNo,
                StudentName = student.Name,
                TotalSubmissionCount = submissions.Count,
                DeletedSubmissionCount = deletedSubmissionCount,
                LatestSubmission = latestSubmission,
                EffectiveSubmission = effective,
                EffectiveStatus = "Needs Review",
                Reason = "No valid submission found. Latest non-deleted on-time submission selected for review.",
                IsReadyForFreeze = false,
                HasLateSubmission = hasLateSubmission,
                HasValidationFailure = hasValidationFailure
            };
        }

        if (nonDeletedSubmissions.Count > 0)
        {
            var lateSubmission = nonDeletedSubmissions.First();

            return new SubmissionSummaryRow
            {
                StudentId = student.Id,
                StudentNo = student.StudentNo,
                StudentName = student.Name,
                TotalSubmissionCount = submissions.Count,
                DeletedSubmissionCount = deletedSubmissionCount,
                LatestSubmission = latestSubmission,
                EffectiveSubmission = lateSubmission,
                EffectiveStatus = "Late Only",
                Reason = "Only late non-deleted submissions exist.",
                IsReadyForFreeze = false,
                HasLateSubmission = true,
                HasValidationFailure = hasValidationFailure
            };
        }

        return new SubmissionSummaryRow
        {
            StudentId = student.Id,
            StudentNo = student.StudentNo,
            StudentName = student.Name,
            TotalSubmissionCount = submissions.Count,
            DeletedSubmissionCount = deletedSubmissionCount,
            LatestSubmission = latestSubmission,
            EffectiveSubmission = null,
            EffectiveStatus = "Missing",
            Reason = submissions.Count == 0
                ? "No submission found."
                : "Only deleted submissions exist.",
            IsReadyForFreeze = false,
            HasLateSubmission = false,
            HasValidationFailure = hasValidationFailure
        };
    }

    private static bool IsValidSubmission(Submission submission)
    {
        if (submission.IsDeleted)
        {
            return false;
        }

        if (submission.Status == "Valid")
        {
            return true;
        }

        return submission.Items.Count > 0 &&
               submission.Items.All(i => i.Status == "Valid");
    }

    private static bool IsValidationFailedSubmission(Submission submission)
    {
        if (submission.IsDeleted)
        {
            return false;
        }

        if (submission.Status == "ValidationFailed")
        {
            return true;
        }

        return submission.Items.Any(i =>
            i.Status is
                "InvalidUrl" or
                "CloneFailed" or
                "CloneTimeout" or
                "DefaultBranchNotFound" or
                "PathNotFound" or
                "MainFileNotFound" or
                "FileNotFound" or
                "RequiredFileMissing" or
                "RequiredFileConfigInvalid" or
                "UnsupportedUrlKind" or
                "SystemError");
    }
}