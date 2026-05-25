using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions.Activities;

public sealed class FinalizeActivity
{
    private readonly IEnvironmentResources _resources;
    private readonly IPollProcessRepository _repository;
    private readonly IAdfPipelineTriggerService _adfTrigger;
    private readonly IPollNotificationService _notification;
    private readonly ILogger<FinalizeActivity> _log;

    public FinalizeActivity(
        IEnvironmentResources resources,
        IPollProcessRepository repository,
        IAdfPipelineTriggerService adfTrigger,
        IPollNotificationService notification,
        ILogger<FinalizeActivity> log)
    {
        _resources = resources;
        _repository = repository;
        _adfTrigger = adfTrigger;
        _notification = notification;
        _log = log;
    }

    [Function("FinalizeActivity")]
    public async Task RunAsync([ActivityTrigger] PollRequest input, CancellationToken cancellationToken)
    {
        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync(cancellationToken);

        var remaining = await _repository.GetWaitingItemsAsync(conn, input.AppId, cancellationToken);
        if (remaining.Count == 0)
        {
            _log.LogInformation("Finalize: no remaining waiting rows for AppId {AppId}", input.AppId);
            return;
        }

        _log.LogWarning(
            "Finalize: {Count} unprocessed row(s) for AppId {AppId}",
            remaining.Count,
            input.AppId);

        await _notification.SendUnprocessedItemsEmailAsync(input.AppId, remaining, cancellationToken);
        await RunAgingJobsAsync(conn, remaining, cancellationToken);
        await RunRemainJobsAsync(conn, remaining, cancellationToken);
    }

    private async Task RunAgingJobsAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        IReadOnlyList<PollItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (!item.AgeThreshold.HasValue || !item.LastRun.HasValue || string.IsNullOrWhiteSpace(item.AgeJobName))
            {
                continue;
            }

            var days = (DateTime.UtcNow - item.LastRun.Value).TotalDays;
            if (days <= item.AgeThreshold.Value)
            {
                continue;
            }

            if (string.Equals(item.AgeJobName, PollProcessConstants.DbaLoopback, StringComparison.OrdinalIgnoreCase))
            {
                await _repository.MarkCompleteAsync(conn, item.Id, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                continue;
            }

            await _repository.MarkActiveForFallbackAsync(conn, item.Id, cancellationToken);
            await _adfTrigger.TriggerAsync(new PollItem { JobName = item.AgeJobName, FileName = item.FileName }, cancellationToken);
            _log.LogInformation("Age-based pipeline {Job} started for item {ItemId}", item.AgeJobName, item.Id);
        }
    }

    private async Task RunRemainJobsAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        IReadOnlyList<PollItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.RemainJobName))
            {
                continue;
            }

            if (string.Equals(item.RemainJobName, PollProcessConstants.DbaLoopback, StringComparison.OrdinalIgnoreCase))
            {
                await _repository.MarkCompleteAsync(conn, item.Id, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                continue;
            }

            await _repository.MarkActiveForFallbackAsync(conn, item.Id, cancellationToken);
            await _adfTrigger.TriggerAsync(new PollItem { JobName = item.RemainJobName, FileName = item.FileName }, cancellationToken);
            _log.LogInformation("Remain pipeline {Job} started for item {ItemId}", item.RemainJobName, item.Id);
        }
    }
}
