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
SET Status = @W
WHERE AppId = @appId
  AND (Status IS NULL OR Status <> @A)", conn);

        cmd.Parameters.AddWithValue("@appId", input.AppId);
        cmd.Parameters.AddWithValue("@W", StatusCodes.Waiting);
        cmd.Parameters.AddWithValue("@A", StatusCodes.Active);

        await cmd.ExecuteNonQueryAsync();
    }
}

