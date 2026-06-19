using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace TabLoadFunction.Services;

public interface IFileShareReader
{
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);

    Task<ShareFileProperties> GetPropertiesAsync(string relativePath, CancellationToken cancellationToken);

    Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken);

    Task DownloadToFileAsync(string relativePath, string destinationPath, CancellationToken cancellationToken);
}

public sealed class FileShareReader : IFileShareReader
{
    private readonly ShareClient _shareClient;

    public FileShareReader(TabLoadFunction.Configuration.IEnvironmentResources resources)
    {
        _shareClient = resources.CreateFileShareClient();
    }

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareExistsAsync(cancellationToken);
        var file = GetFileClient(relativePath);
        return await file.ExistsAsync(cancellationToken);
    }

    public async Task<ShareFileProperties> GetPropertiesAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareExistsAsync(cancellationToken);
        var file = GetFileClient(relativePath);
        var response = await file.GetPropertiesAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<string> ReadAllTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareExistsAsync(cancellationToken);
        var file = GetFileClient(relativePath);
        var download = await file.DownloadAsync(cancellationToken: cancellationToken);
        using var reader = new StreamReader(download.Value.Content);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task DownloadToFileAsync(string relativePath, string destinationPath, CancellationToken cancellationToken)
    {
        await EnsureShareExistsAsync(cancellationToken);
        var file = GetFileClient(relativePath);
        var download = await file.DownloadAsync(cancellationToken: cancellationToken);
        await using var destination = File.Create(destinationPath);
        await download.Value.Content.CopyToAsync(destination, cancellationToken);
    }

    private async Task EnsureShareExistsAsync(CancellationToken cancellationToken)
    {
        await _shareClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    private ShareFileClient GetFileClient(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex < 0)
        {
            return _shareClient.GetRootDirectoryClient().GetFileClient(normalized);
        }

        var directoryPath = normalized[..slashIndex];
        var fileName = normalized[(slashIndex + 1)..];
        var directory = _shareClient.GetDirectoryClient(directoryPath);
        return directory.GetFileClient(fileName);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');
}
