using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions.Activities;

public sealed class TriggerReadyJobsActivity
{
    private readonly IEnvironmentResources _resources;
    private readonly IPollProcessRepository _repository;
    private readonly IAdfPipelineTriggerService _adfTrigger;
    private readonly IAdfPipelineMetadataService _metadataService;
    private readonly ILogger<TriggerReadyJobsActivity> _log;

    public TriggerReadyJobsActivity(
        IEnvironmentResources resources,
        IPollProcessRepository repository,
        IAdfPipelineTriggerService adfTrigger,
        IAdfPipelineMetadataService metadataService,
        ILogger<TriggerReadyJobsActivity> log)
    {
        _resources = resources;
        _repository = repository;
        _adfTrigger = adfTrigger;
        _metadataService = metadataService;
        _log = log;
    }

    [Function("TriggerReadyJobsActivity")]
    public async Task<PipelineTriggerResult> RunAsync(
        [ActivityTrigger] IReadOnlyList<PipelineTriggerItem> items,
        CancellationToken cancellationToken)
    {
        var triggered = new List<string>();
        var failures = new List<PipelineTriggerFailure>();

        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync(cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.JobName))
            {
                failures.Add(new PipelineTriggerFailure
                {
                    Id = item.Id,
                    FileName = item.FileName,
                    Reason = "JobName is empty."
                });
                await _repository.MarkFailedAsync(conn, item.Id, cancellationToken);
                continue;
            }

            if (string.Equals(item.JobName, PollProcessConstants.DbaLoopback, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("Skipping ADF trigger for loopback item {ItemId}", item.Id);
                continue;
            }

            try
            {
                var pipeline = await _metadataService.GetPipelineDetailsAsync(item.JobName, cancellationToken);
                if (pipeline is null)
                {
                    failures.Add(new PipelineTriggerFailure
                    {
                        Id = item.Id,
                        JobName = item.JobName,
                        FileName = item.FileName,
                        Reason = "ADF pipeline was not found."
                    });

                    _log.LogError(
                        "ADF pipeline {Pipeline} was not found for poll item {ItemId}",
                        item.JobName,
                        item.Id);

                    await _repository.MarkFailedAsync(conn, item.Id, cancellationToken);
                    continue;
                }

                await _adfTrigger.TriggerAsync(new PollItem
                {
                    Id = item.Id,
                    JobName = item.JobName,
                    FileName = item.FileName
                }, cancellationToken);

                triggered.Add(item.JobName);

                _log.LogInformation(
                    "Triggered ready ADF pipeline {Pipeline} for poll item {ItemId}",
                    item.JobName,
                    item.Id);
            }
            catch (Exception ex)
            {
                failures.Add(new PipelineTriggerFailure
                {
                    Id = item.Id,
                    JobName = item.JobName,
                    FileName = item.FileName,
                    Reason = ex.Message
                });

                _log.LogError(
                    ex,
                    "Failed to trigger ADF pipeline {Pipeline} for poll item {ItemId}",
                    item.JobName,
                    item.Id);

                await _repository.MarkFailedAsync(conn, item.Id, cancellationToken);
            }
        }

        return new PipelineTriggerResult
        {
            TriggeredPipelines = triggered,
            FailedTriggers = failures
        };
    }
}
