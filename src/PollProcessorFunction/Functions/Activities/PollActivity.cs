using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions.Activities;

public sealed class PollActivity
{
    private readonly IEnvironmentResources _resources;
    private readonly IPollProcessRepository _repository;
    private readonly IFileReadyChecker _fileReadyChecker;
    private readonly ILogger<PollActivity> _log;

    public PollActivity(
        IEnvironmentResources resources,
        IPollProcessRepository repository,
        IFileReadyChecker fileReadyChecker,
        ILogger<PollActivity> log)
    {
        _resources = resources;
        _repository = repository;
        _fileReadyChecker = fileReadyChecker;
        _log = log;
    }

    [Function("PollActivity")]
    public async Task<PollActivityResult> RunAsync([ActivityTrigger] PollRequest input, CancellationToken cancellationToken)
    {
        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync(cancellationToken);

        var concurrency = await _repository.GetConcurrencyLimitAsync(conn, input.AppId, cancellationToken);
        var active = await _repository.GetActiveCountAsync(conn, input.AppId, cancellationToken);

        if (active >= concurrency)
        {
            _log.LogInformation(
                "AppId {AppId} at concurrency limit ({Active}/{Limit})",
                input.AppId,
                active,
                concurrency);
            return new PollActivityResult { NoWaitingItems = false };
        }

        var waiting = await _repository.GetWaitingItemsAsync(conn, input.AppId, cancellationToken);
        if (waiting.Count == 0)
        {
            _log.LogInformation("AppId {AppId} has no waiting poll_process rows", input.AppId);
            return new PollActivityResult { NoWaitingItems = true };
        }

        var share = _resources.CreateFileShareClient();
        var slots = concurrency - active;
        var jobsToTrigger = new List<string>();
        var pipelineTriggers = new List<PipelineTriggerItem>();

        foreach (var item in waiting.Take(slots))
        {
            var mode = ResolveMode(item, input);
            var conditionMet = await IsConditionMetAsync(share, conn, item, mode, cancellationToken);

            if (!conditionMet)
            {
                continue;
            }

            if (!await _repository.TryMarkActiveAsync(conn, item.Id, cancellationToken))
            {
                continue;
            }

            var jobName = await HandOffReadyItemAsync(conn, item, cancellationToken);
            if (!string.IsNullOrWhiteSpace(jobName))
            {
                jobsToTrigger.Add(jobName);
                pipelineTriggers.Add(new PipelineTriggerItem
                {
                    Id = item.Id,
                    JobName = jobName,
                    FileName = item.FileName
                });
            }
        }

        return new PollActivityResult
        {
            NoWaitingItems = false,
            JobsToTrigger = jobsToTrigger,
            PipelineTriggers = pipelineTriggers
        };
    }

    private async Task<bool> IsConditionMetAsync(
        Azure.Storage.Files.Shares.ShareClient share,
        Microsoft.Data.SqlClient.SqlConnection conn,
        PollItem item,
        string mode,
        CancellationToken cancellationToken)
    {
        if (mode == "S")
        {
            var evaluation = await _repository.EvaluateSqlConditionAsync(conn, item, cancellationToken);
            if (!evaluation.IsMet)
            {
                _log.LogInformation(
                    "Poll item {ItemId} SQL condition not met. Expected='{Expected}', Actual='{Actual}'",
                    item.Id,
                    evaluation.ExpectedValue,
                    evaluation.ActualValue);
            }

            return evaluation.IsMet;
        }

        if (mode == "F")
        {
            var fileReady = await _fileReadyChecker.IsConditionMetAsync(share, item, cancellationToken);
            if (!fileReady)
            {
                _log.LogDebug(
                    "Poll item {ItemId} file not ready yet (file {FileName})",
                    item.Id,
                    item.FileName);
            }

            return fileReady;
        }

        _log.LogWarning("Poll item {ItemId} has unknown mode '{Mode}'; skipping", item.Id, mode);
        return false;
    }

    /// <summary>
    /// Condition met: keep poll_process active (or complete for loopback) and return the job name for external triggering.
    /// ADF is not invoked here; tabload sets C/F when childProcess=Y.
    /// </summary>
    private async Task<string?> HandOffReadyItemAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        PollItem item,
        CancellationToken cancellationToken)
    {
        if (string.Equals(item.JobName, PollProcessConstants.DbaLoopback, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("DBA_LOOPBACK for poll item {ItemId}; marking poll_process complete", item.Id);
            await _repository.MarkCompleteAsync(conn, item.Id, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return item.JobName;
        }

        _log.LogInformation(
            "Poll item {ItemId} ready to trigger job {JobName} (file {FileName}); poll_process left active",
            item.Id,
            item.JobName,
            item.FileName);

        return item.JobName;
    }

    private static string ResolveMode(PollItem item, PollRequest input) =>
        string.IsNullOrWhiteSpace(item.Mode) ? input.Mode : item.Mode;
}
