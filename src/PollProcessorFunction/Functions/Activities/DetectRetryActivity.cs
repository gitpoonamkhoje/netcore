using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions.Activities;

public sealed class DetectRetryActivity
{
    private readonly IEnvironmentResources _resources;

    public DetectRetryActivity(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    [Function("DetectRetryActivity")]
    public async Task<bool> RunAsync([ActivityTrigger] PollRequest input)
    {
        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT COUNT(*)
FROM poll_process
WHERE (AppId = @appId OR app_id = @appId)
  AND (Status = @active OR jobstat_tx = @active)", conn);

        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);

        cmd.Parameters.AddWithValue("@appId", input.AppId);

        int count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        return count > 0;
    }
}

