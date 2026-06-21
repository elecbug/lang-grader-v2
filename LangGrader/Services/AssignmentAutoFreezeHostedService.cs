using LangGrader.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LangGrader.Services;

public sealed class AssignmentAutoFreezeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssignmentAutoFreezeHostedService> _logger;
    private readonly AutoFreezeOptions _options;

    public AssignmentAutoFreezeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AssignmentAutoFreezeHostedService> logger,
        IOptions<AutoFreezeOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Assignment auto freeze is disabled.");
            return;
        }

        var startupDelaySeconds = Math.Max(0, _options.StartupDelaySeconds);
        var checkIntervalSeconds = Math.Clamp(_options.CheckIntervalSeconds, 30, 3600);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assignment auto freeze check failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var freezeService = scope.ServiceProvider.GetRequiredService<IAssignmentFreezeService>();

        var nowUtc = DateTime.UtcNow;
        var batchSize = Math.Clamp(_options.BatchSize, 1, 100);

        var overdueAssignments = await db.Assignments
            .AsNoTracking()
            .Where(a =>
                a.IsPublished &&
                a.AutoFreezeEnabled &&
                !a.IsFrozen &&
                a.DeadlineAt <= nowUtc)
            .OrderBy(a => a.DeadlineAt)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.DeadlineAt,
                a.AutoFreezeDelayMinutes
            })
            .ToListAsync(stoppingToken);

        var candidates = overdueAssignments
            .Where(a =>
                a.DeadlineAt.AddMinutes(Math.Max(0, a.AutoFreezeDelayMinutes)) <= nowUtc)
            .Take(batchSize)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var assignment in candidates)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation(
                    "Auto freezing assignment {AssignmentId}: {AssignmentTitle}",
                    assignment.Id,
                    assignment.Title
                );

                var result = await freezeService.FreezeAssignmentAsync(assignment.Id);

                _logger.LogInformation(
                    "Auto freeze completed for assignment {AssignmentId}. Frozen={FrozenCount}, Failed={FailedCount}, Messages={Messages}",
                    assignment.Id,
                    result.FrozenSubmissionCount,
                    result.FailedSubmissionCount,
                    string.Join(" ", result.Messages)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Auto freeze failed for assignment {AssignmentId}: {AssignmentTitle}",
                    assignment.Id,
                    assignment.Title
                );
            }
        }
    }
}