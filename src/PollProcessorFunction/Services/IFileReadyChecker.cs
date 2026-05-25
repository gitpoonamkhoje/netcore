using Azure.Storage.Files.Shares;

namespace PollProcessorFunction.Services;

public interface IFileReadyChecker
{
    Task<bool> IsConditionMetAsync(ShareClient share, PollProcessorFunction.Models.PollItem item, CancellationToken cancellationToken = default);
}
