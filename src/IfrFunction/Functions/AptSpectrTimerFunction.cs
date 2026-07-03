using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using IfrFunction.Configuration;
using IfrFunction.Models;
using IfrFunction.Services;

namespace IfrFunction.Functions;

public sealed class AptSpectrTimerFunction
{
    private readonly IEnvironmentResources _resources;
    private readonly IAptSpectrService _service;
    private readonly ILogger<AptSpectrTimerFunction> _logger;

    public AptSpectrTimerFunction(
        IEnvironmentResources resources,
        IAptSpectrService service,
        ILogger<AptSpectrTimerFunction> logger)
    {
        _resources = resources;
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Polls APT and SPECTR inbound folders on Azure File Share (diagram section 2).
    /// </summary>
    [Function("AptSpectrPoll")]
    public async Task RunAsync(
        [TimerTrigger("%AppResources:AptSpectrPollCron%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!_resources.AptSpectrPollingEnabled)
        {
            _logger.LogDebug("APT/SPECTR polling is disabled (AppResources:AptSpectrPollingEnabled=false).");
            return;
        }

        _logger.LogInformation("APT/SPECTR poll started at {Time}", DateTime.UtcNow);

        var aptResult = await _service.ProcessAsync(new AptSpectrRequest
        {
            Source = AptSpectrSources.Apt,
            InputDirectory = _resources.AptInputSharePath,
            ApplyTransformations = _resources.AptApplyTransformations,
            SkipAlreadyProcessed = true,
            MoveToProcessedFolder = true
        }, cancellationToken);

        var spectrResult = await _service.ProcessAsync(new AptSpectrRequest
        {
            Source = AptSpectrSources.Spectr,
            InputDirectory = _resources.SpectrInputSharePath,
            ApplyTransformations = _resources.SpectrApplyTransformations,
            SkipAlreadyProcessed = true,
            MoveToProcessedFolder = true
        }, cancellationToken);

        _logger.LogInformation(
            "APT/SPECTR poll complete. APT success={AptSuccess}, SPECTR success={SpectrSuccess}",
            aptResult.Succeeded,
            spectrResult.Succeeded);
    }
}
