using Azure.Storage.Blobs.Models;
using IfrFunction.Models;

namespace IfrFunction.Services;

public interface IBlobStorageService
{
    Task DownloadToFileAsync(string blobPath, string localFilePath, CancellationToken cancellationToken);

    Task MoveAsync(
        string sourceBlobPath,
        string destinationBlobPath,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    Task<int> GetRetryCountAsync(string blobPath, CancellationToken cancellationToken);

    Task<BlobProperties> GetPropertiesAsync(string blobPath, CancellationToken cancellationToken);
}

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly Configuration.IEnvironmentResources _resources;

    public BlobStorageService(Configuration.IEnvironmentResources resources)
    {
        _resources = resources;
    }

    public async Task DownloadToFileAsync(
        string blobPath,
        string localFilePath,
        CancellationToken cancellationToken)
    {
        var client = GetBlobClient(blobPath);
        await using var stream = File.Create(localFilePath);
        await client.DownloadToAsync(stream, cancellationToken);
    }

    public async Task MoveAsync(
        string sourceBlobPath,
        string destinationBlobPath,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var source = GetBlobClient(sourceBlobPath);
        var destination = GetBlobClient(destinationBlobPath);

        await using (var readStream = await OpenReadAsync(source, cancellationToken))
        {
            await destination.UploadAsync(readStream, overwrite: true, cancellationToken: cancellationToken);
        }

        if (metadata is not null)
        {
            await destination.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
        }

        await source.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    public async Task<int> GetRetryCountAsync(string blobPath, CancellationToken cancellationToken)
    {
        var client = GetBlobClient(blobPath);
        if (!await client.ExistsAsync(cancellationToken))
        {
            return 0;
        }

        var properties = await client.GetPropertiesAsync(cancellationToken: cancellationToken);
        if (properties.Value.Metadata.TryGetValue(AptSpectrBlobFolders.RetryMetadataKey, out var value) &&
            int.TryParse(value, out var count))
        {
            return count;
        }

        return 0;
    }

    public async Task<BlobProperties> GetPropertiesAsync(string blobPath, CancellationToken cancellationToken)
    {
        var response = await GetBlobClient(blobPath).GetPropertiesAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    private Azure.Storage.Blobs.BlobClient GetBlobClient(string blobPath)
    {
        var normalized = Models.AptSpectrBlobPathHelper.Normalize(blobPath);
        return _resources.CreateBlobContainerClient().GetBlobClient(normalized);
    }

    private static async Task<Stream> OpenReadAsync(
        Azure.Storage.Blobs.BlobClient client,
        CancellationToken cancellationToken)
    {
        var response = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }
}
