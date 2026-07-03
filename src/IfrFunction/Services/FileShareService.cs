using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace IfrFunction.Services;

public interface IFileShareService
{
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);

    Task<ShareFileProperties> GetPropertiesAsync(string relativePath, CancellationToken cancellationToken);

    Task DownloadToFileAsync(string relativePath, string destinationPath, CancellationToken cancellationToken);

    Task UploadFromFileAsync(string relativePath, string localFilePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, CancellationToken cancellationToken);

    Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed class FileShareService : IFileShareService
{
    private readonly ShareClient _share;

    public FileShareService(Configuration.IEnvironmentResources resources)
    {
        _share = resources.CreateFileShareClient();
    }

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        return await GetFileClient(relativePath).ExistsAsync(cancellationToken);
    }

    public async Task<ShareFileProperties> GetPropertiesAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        var response = await GetFileClient(relativePath).GetPropertiesAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task DownloadToFileAsync(string relativePath, string destinationPath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        var download = await GetFileClient(relativePath).DownloadAsync(cancellationToken: cancellationToken);
        await using var destination = File.Create(destinationPath);
        await download.Value.Content.CopyToAsync(destination, cancellationToken);
    }

    public async Task UploadFromFileAsync(string relativePath, string localFilePath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        var (directoryClient, fileName) = GetDirectoryAndFileName(relativePath);
        await directoryClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var fileClient = directoryClient.GetFileClient(fileName);
        await using var stream = File.OpenRead(localFilePath);
        await fileClient.CreateAsync(stream.Length, cancellationToken: cancellationToken);
        await fileClient.UploadAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        var normalized = NormalizePath(directoryPath);
        var directory = string.IsNullOrEmpty(normalized)
            ? _share.GetRootDirectoryClient()
            : _share.GetDirectoryClient(normalized);

        if (!await directory.ExistsAsync(cancellationToken))
        {
            return Array.Empty<string>();
        }

        var files = new List<string>();
        await foreach (var item in directory.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
        {
            if (!item.IsDirectory)
            {
                files.Add(string.IsNullOrEmpty(normalized) ? item.Name : $"{normalized}/{item.Name}");
            }
        }

        return files;
    }

    public async Task MoveAsync(string sourceRelativePath, string destinationRelativePath, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{Path.GetFileName(sourceRelativePath)}");
        try
        {
            await DownloadToFileAsync(sourceRelativePath, tempPath, cancellationToken);
            await UploadFromFileAsync(destinationRelativePath, tempPath, cancellationToken);
            await DeleteAsync(sourceRelativePath, cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        await EnsureShareAsync(cancellationToken);
        await GetFileClient(relativePath).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private async Task EnsureShareAsync(CancellationToken cancellationToken) =>
        await _share.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    private ShareFileClient GetFileClient(string relativePath)
    {
        var (directoryClient, fileName) = GetDirectoryAndFileName(relativePath);
        return directoryClient.GetFileClient(fileName);
    }

    private (ShareDirectoryClient Directory, string FileName) GetDirectoryAndFileName(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex < 0)
        {
            return (_share.GetRootDirectoryClient(), normalized);
        }

        var directoryPath = normalized[..slashIndex];
        var fileName = normalized[(slashIndex + 1)..];
        return (_share.GetDirectoryClient(directoryPath), fileName);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');
}
