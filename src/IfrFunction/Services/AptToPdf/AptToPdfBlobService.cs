using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfBlobService
{
    Task EnsureContainersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, CancellationToken cancellationToken);

    Task DownloadAsync(string containerName, string blobName, string localFilePath, CancellationToken cancellationToken);

    Task UploadFileAsync(string containerName, string blobName, string localFilePath, CancellationToken cancellationToken);

    Task UploadTextAsync(string containerName, string blobName, string content, CancellationToken cancellationToken);

    Task MoveAsync(
        string sourceContainer,
        string sourceBlobName,
        string destinationContainer,
        string destinationBlobName,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    Task<int> GetRetryCountAsync(string containerName, string blobName, CancellationToken cancellationToken);

    Task<BlobProperties> GetPropertiesAsync(string containerName, string blobName, CancellationToken cancellationToken);
}

public sealed class AptToPdfBlobService : IAptToPdfBlobService
{
    private readonly Configuration.IEnvironmentResources _resources;

    public AptToPdfBlobService(Configuration.IEnvironmentResources resources)
    {
        _resources = resources;
    }

    public async Task EnsureContainersAsync(CancellationToken cancellationToken)
    {
        foreach (var container in GetAllContainers())
        {
            await _resources.CreateBlobContainerClient(container)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, CancellationToken cancellationToken)
    {
        var container = _resources.CreateBlobContainerClient(containerName);
        if (!await container.ExistsAsync(cancellationToken))
        {
            return Array.Empty<string>();
        }

        var blobs = new List<string>();
        await foreach (var item in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            blobs.Add(item.Name);
        }

        return blobs;
    }

    public async Task DownloadAsync(
        string containerName,
        string blobName,
        string localFilePath,
        CancellationToken cancellationToken)
    {
        var client = GetBlobClient(containerName, blobName);
        await using var stream = File.Create(localFilePath);
        await client.DownloadToAsync(stream, cancellationToken);
    }

    public async Task UploadFileAsync(
        string containerName,
        string blobName,
        string localFilePath,
        CancellationToken cancellationToken)
    {
        var client = GetBlobClient(containerName, blobName);
        await using var stream = File.OpenRead(localFilePath);
        await client.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
    }

    public async Task UploadTextAsync(
        string containerName,
        string blobName,
        string content,
        CancellationToken cancellationToken)
    {
        var client = GetBlobClient(containerName, blobName);
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await client.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
    }

    public async Task MoveAsync(
        string sourceContainer,
        string sourceBlobName,
        string destinationContainer,
        string destinationBlobName,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var source = GetBlobClient(sourceContainer, sourceBlobName);
        var destination = GetBlobClient(destinationContainer, destinationBlobName);

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

    public async Task<int> GetRetryCountAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken)
    {
        var client = GetBlobClient(containerName, blobName);
        if (!await client.ExistsAsync(cancellationToken))
        {
            return 0;
        }

        var properties = await client.GetPropertiesAsync(cancellationToken: cancellationToken);
        if (properties.Value.Metadata.TryGetValue(Models.AptToPdfBlobMetadata.RetryCount, out var value) &&
            int.TryParse(value, out var count))
        {
            return count;
        }

        return 0;
    }

    public async Task<BlobProperties> GetPropertiesAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken)
    {
        var response = await GetBlobClient(containerName, blobName).GetPropertiesAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    private IEnumerable<string> GetAllContainers() =>
    [
        _resources.AptToPdfInputContainer,
        _resources.AptToPdfProcessingContainer,
        _resources.AptToPdfOutputContainer,
        _resources.AptToPdfArchiveContainer,
        _resources.AptToPdfFailedContainer,
        _resources.AptToPdfLogsContainer
    ];

    private BlobClient GetBlobClient(string containerName, string blobName) =>
        _resources.CreateBlobContainerClient(containerName).GetBlobClient(blobName);

    private static async Task<Stream> OpenReadAsync(BlobClient client, CancellationToken cancellationToken)
    {
        var response = await client.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }
}
