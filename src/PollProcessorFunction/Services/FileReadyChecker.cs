using Azure.Storage.Files.Shares;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class FileReadyChecker : IFileReadyChecker
{
    public async Task<bool> IsConditionMetAsync(
        ShareClient share,
        PollItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            return false;
        }

        var directoryPath = (item.FilePath ?? "").Trim().TrimEnd('/');
        var directory = string.IsNullOrEmpty(directoryPath)
            ? share.GetRootDirectoryClient()
            : share.GetDirectoryClient(directoryPath);

        var file = directory.GetFileClient(item.FileName);
        if (!await file.ExistsAsync(cancellationToken))
        {
            return false;
        }

        return await IsFileReadyAsync(file, cancellationToken);
    }

    private static async Task<bool> IsFileReadyAsync(
        Azure.Storage.Files.Shares.ShareFileClient file,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await file.OpenReadAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
