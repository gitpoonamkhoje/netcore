using Microsoft.Data.SqlClient;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class PollProcessRepository : IPollProcessRepository
{
    public async Task<int> GetConcurrencyLimitAsync(
        SqlConnection conn,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var value = await GetParmValueAsync(conn, appId, PollProcessConstants.DefaultConcurrencyParam, cancellationToken);
        return value != null && int.TryParse(value, out var limit)
            ? limit
            : PollProcessConstants.DefaultConcurrency;
    }

    public async Task<int> GetActiveCountAsync(
        SqlConnection conn,
        string appId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
SELECT COUNT(*)
FROM poll_process
WHERE (AppId = @appId OR app_id = @appId)
  AND (Status = @active OR jobstat_tx = @active)", conn);

        cmd.Parameters.AddWithValue("@appId", appId);
        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<PollItem>> GetWaitingItemsAsync(
        SqlConnection conn,
        string appId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
SELECT *
FROM poll_process
WHERE (AppId = @appId OR app_id = @appId)
  AND (Status = @waiting OR jobstat_tx = @waiting)", conn);

        cmd.Parameters.AddWithValue("@appId", appId);
        cmd.Parameters.AddWithValue("@waiting", StatusCodes.Waiting);

        var items = new List<PollItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapPollItem(reader));
        }

        return items;
    }

    public async Task<bool> TryMarkActiveAsync(
        SqlConnection conn,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @active,
    jobstat_tx = @active,
    LastRun = GETDATE(),
    AgeLastRun_dt = GETDATE()
WHERE Id = @id
  AND (Status = @waiting OR jobstat_tx = @waiting)", conn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);
        cmd.Parameters.AddWithValue("@waiting", StatusCodes.Waiting);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task MarkCompleteAsync(
        SqlConnection conn,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @complete,
    jobstat_tx = @complete,
    AgeLastRun_dt = GETDATE()
WHERE Id = @id", conn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@complete", StatusCodes.Complete);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevertToWaitingAsync(
        SqlConnection conn,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @waiting,
    jobstat_tx = @waiting
WHERE Id = @id
  AND (Status = @active OR jobstat_tx = @active)", conn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@waiting", StatusCodes.Waiting);
        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkActiveForFallbackAsync(
        SqlConnection conn,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(@"
UPDATE poll_process
SET Status = @active,
    jobstat_tx = @active,
    AgeLastRun_dt = GETDATE()
WHERE Id = @id", conn);

        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@active", StatusCodes.Active);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> EvaluateSqlConditionAsync(
        SqlConnection conn,
        PollItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.SqlQuery))
        {
            return false;
        }

        await using var cmd = new SqlCommand(item.SqlQuery, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        var actual = result?.ToString();
        var expected = item.ExpectedValue?.ToString();
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static PollItem MapPollItem(SqlDataReader reader)
    {
        var lastRun = reader.GetDateTimeOrNull("LastRun", "AgeLastRun_dt");
        var ageThreshold = reader.GetInt32OrNull("AgeThreshold", "AgeChk_cd");

        return new PollItem
        {
            Id = reader.GetInt32OrNull("Id") ?? 0,
            FileName = reader.GetStringOrNull("FileName", "filename_tx"),
            FilePath = reader.GetStringOrNull("FilePath", "filepath_tx"),
            JobName = reader.GetStringOrNull("JobName", "runjob_tx"),
            Mode = reader.GetStringOrNull("Mode", "mode_cd"),
            SqlQuery = reader.GetStringOrNull("SqlQuery", "sqlcmd_tx"),
            ExpectedValue = reader.GetStringOrNull("ExpectedValue", "sqlcmd_val"),
            LastRun = lastRun,
            AgeThreshold = ageThreshold,
            AgeJobName = reader.GetStringOrNull("AgeJobName", "AgeJob_tx"),
            RemainJobName = reader.GetStringOrNull("RemainJobName", "RemainJob_tx")
        };
    }

    private static async Task<string?> GetParmValueAsync(
        SqlConnection conn,
        string appId,
        string paramName,
        CancellationToken cancellationToken)
    {
        await using var modern = new SqlCommand(@"
SELECT TOP 1 ParamValue
FROM poll_parm
WHERE AppId = @appId AND ParamName = @name", conn);
        modern.Parameters.AddWithValue("@appId", appId);
        modern.Parameters.AddWithValue("@name", paramName);

        var modernValue = await modern.ExecuteScalarAsync(cancellationToken);
        if (modernValue is not null and not DBNull)
        {
            return modernValue.ToString();
        }

        await using var legacy = new SqlCommand(@"
SELECT TOP 1 parm_val
FROM poll_parm
WHERE app_id = @appId AND parm_tx = @name", conn);
        legacy.Parameters.AddWithValue("@appId", appId);
        legacy.Parameters.AddWithValue("@name", paramName);

        var legacyValue = await legacy.ExecuteScalarAsync(cancellationToken);
        return legacyValue is null or DBNull ? null : legacyValue.ToString();
    }
}
