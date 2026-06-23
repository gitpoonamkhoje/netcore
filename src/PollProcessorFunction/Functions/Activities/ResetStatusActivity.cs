using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions.Activities;

public sealed class ResetStatusActivity
{
    private readonly IEnvironmentResources _resources;

    public ResetStatusActivity(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    [Function("ResetStatusActivity")]
    public async Task RunAsync([ActivityTrigger] PollRequest input)
    {
        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @W,
    jobstat_tx = @W
WHERE (AppId = @appId OR app_id = @appId)
  AND (jobstat_tx <> @I OR jobstat_tx IS NULL)
  AND (Status <> @I OR Status IS NULL)", conn);

        cmd.Parameters.AddWithValue("@appId", input.AppId);
        cmd.Parameters.AddWithValue("@W", StatusCodes.Waiting);
        cmd.Parameters.AddWithValue("@I", StatusCodes.Inactive);

        await cmd.ExecuteNonQueryAsync();
    }
}

