using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IPollNotificationService
{
    Task SendUnprocessedItemsEmailAsync(string appId, IReadOnlyList<PollItem> items, CancellationToken cancellationToken = default);
}
