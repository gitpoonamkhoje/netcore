using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions.Activities;

public sealed class FixIncompleteActivity
{
    private readonly IEnvironmentResources _resources;
    private readonly ILogger<FixIncompleteActivity> _log;

    public FixIncompleteActivity(IEnvironmentResources resources, ILogger<FixIncompleteActivity> log)
    {
        _resources = resources;
        _log = log;
    }

    [Function("FixIncompleteActivity")]
    public async Task RunAsync([ActivityTrigger] PollRequest input)
    {
        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @waiting,
    jobstat_tx = @waiting
WHERE (AppId = @appId OR app_id = @appId)
  AND (Status = @active OR jobstat_tx = @active)", conn);

        cmd.Parameters.AddWithValue("@appId", input.AppId);
        cmd.Parameters.AddWithValue("@waiting", StatusCodes.Waiting);
        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);
        await cmd.ExecuteNonQueryAsync();

        _log.LogWarning("Retry detected; resetting 'A' records back to 'W' for AppId {AppId}", input.AppId);
    }
}

