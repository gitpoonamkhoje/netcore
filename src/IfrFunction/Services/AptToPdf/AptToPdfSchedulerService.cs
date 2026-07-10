using Microsoft.Extensions.Logging;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfSchedulerService
{
    Task<AptToPdfPipelineResult?> TryRunScheduledJobAsync(CancellationToken cancellationToken);
}

/// <summary>
/// DB-driven scheduler from diagram: reads JOB_SCHEDULE and runs pipeline when due.
/// </summary>
public sealed class AptToPdfSchedulerService : IAptToPdfSchedulerService
{
    private readonly IEnvironmentResources _resources;
    private readonly IAptToPdfRepository _repository;
    private readonly IAptToPdfPipelineService _pipeline;
    private readonly ILogger<AptToPdfSchedulerService> _logger;

    public AptToPdfSchedulerService(
        IEnvironmentResources resources,
        IAptToPdfRepository repository,
        IAptToPdfPipelineService pipeline,
        ILogger<AptToPdfSchedulerService> logger)
    {
        _resources = resources;
        _repository = repository;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task<AptToPdfPipelineResult?> TryRunScheduledJobAsync(CancellationToken cancellationToken)
    {
        if (!_resources.AptToPdfEnabled)
        {
            return null;
        }

        await _repository.EnsureTablesAsync(cancellationToken);

        if (_resources.AptToPdfUseDatabaseScheduler)
        {
            var schedule = await _repository.GetJobScheduleAsync(_resources.AptToPdfJobName, cancellationToken);
            if (schedule is null || !schedule.Enabled)
            {
                _logger.LogDebug("APT-to-PDF job is not enabled or not configured.");
                return null;
            }

            if (schedule.NextRunTimeUtc > DateTime.UtcNow)
            {
                _logger.LogDebug(
                    "APT-to-PDF not due yet. NextRunTimeUtc={NextRun}",
                    schedule.NextRunTimeUtc);
                return null;
            }

            if (string.Equals(schedule.Status, AptToPdfStatuses.Running, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("APT-to-PDF job is already running.");
                return null;
            }
        }

        _logger.LogInformation("Starting scheduled APT-to-PDF pipeline.");
        var result = await _pipeline.RunAsync(cancellationToken);
        _logger.LogInformation(
            "APT-to-PDF pipeline finished. Success={Success}, Total={Total}, Failed={Failed}",
            result.Succeeded,
            result.TotalFiles,
            result.FailedFiles);

        return result;
    }
}
