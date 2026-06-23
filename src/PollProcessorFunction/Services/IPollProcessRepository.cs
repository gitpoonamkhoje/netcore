using Microsoft.Data.SqlClient;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IPollProcessRepository
{
    Task<int> GetConcurrencyLimitAsync(SqlConnection conn, string appId, CancellationToken cancellationToken = default);

    Task<int> GetActiveCountAsync(SqlConnection conn, string appId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PollItem>> GetWaitingItemsAsync(SqlConnection conn, string appId, CancellationToken cancellationToken = default);

    Task<bool> TryMarkActiveAsync(SqlConnection conn, int id, CancellationToken cancellationToken = default);

    Task MarkCompleteAsync(SqlConnection conn, int id, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(SqlConnection conn, int id, CancellationToken cancellationToken = default);

    Task<int> MarkActiveRowsFailedForAppAsync(SqlConnection conn, string appId, CancellationToken cancellationToken = default);

    Task RevertToWaitingAsync(SqlConnection conn, int id, CancellationToken cancellationToken = default);

    Task<SqlConditionEvaluation> EvaluateSqlConditionAsync(SqlConnection conn, PollItem item, CancellationToken cancellationToken = default);

    Task MarkActiveForFallbackAsync(SqlConnection conn, int id, CancellationToken cancellationToken = default);
}
