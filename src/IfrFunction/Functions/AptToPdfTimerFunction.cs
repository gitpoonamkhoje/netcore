using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using IfrFunction.Services.AptToPdf;

namespace IfrFunction.Functions;

public sealed class AptToPdfTimerFunction
{
    private readonly IAptToPdfSchedulerService _scheduler;
    private readonly ILogger<AptToPdfTimerFunction> _logger;

    public AptToPdfTimerFunction(IAptToPdfSchedulerService scheduler, ILogger<AptToPdfTimerFunction> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    /// <summary>
    /// Polls JOB_SCHEDULE every 5 minutes; runs APT-to-PDF when NextRunTimeUtc is due (diagram: 8 PM daily default).
    /// </summary>
    [Function("AptToPdfScheduler")]
    public async Task RunAsync(
        [TimerTrigger("%AppResources:AptToPdfSchedulerCron%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("APT-to-PDF scheduler tick at {Time}", DateTime.UtcNow);
        await _scheduler.TryRunScheduledJobAsync(cancellationToken);
    }
}
