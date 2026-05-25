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
SET Status = 'W'
WHERE AppId = @appId
  AND Status = 'A'", conn);

        cmd.Parameters.AddWithValue("@appId", input.AppId);
        await cmd.ExecuteNonQueryAsync();

        _log.LogWarning("Retry detected; resetting 'A' records back to 'W' for AppId {AppId}", input.AppId);
    }
}

