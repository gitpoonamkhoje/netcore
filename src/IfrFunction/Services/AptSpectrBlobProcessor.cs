using Microsoft.Extensions.Logging;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services;

public interface IAptSpectrBlobProcessor
{
    Task ProcessBlobAsync(string blobPath, CancellationToken cancellationToken);
}

/// <summary>
/// Event-driven APT/SPECTR processing from blob storage with retry and error-folder routing.
/// </summary>
public sealed class AptSpectrBlobProcessor : IAptSpectrBlobProcessor
{
    private readonly IEnvironmentResources _resources;
    private readonly IBlobStorageService _blobStorage;
    private readonly IAptSpectrService _aptSpectrService;
    private readonly IAptSpectrMetadataRepository _metadata;
    private readonly ILogger<AptSpectrBlobProcessor> _logger;

    public AptSpectrBlobProcessor(
        IEnvironmentResources resources,
        IBlobStorageService blobStorage,
        IAptSpectrService aptSpectrService,
        IAptSpectrMetadataRepository metadata,
        ILogger<AptSpectrBlobProcessor> logger)
    {
        _resources = resources;
        _blobStorage = blobStorage;
        _aptSpectrService = aptSpectrService;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task ProcessBlobAsync(string blobPath, CancellationToken cancellationToken)
    {
        var normalizedPath = AptSpectrBlobPathHelper.Normalize(blobPath);
        if (!TryParseBlobPath(normalizedPath, out var sourceKey, out var stage, out var fileName))
        {
            _logger.LogWarning("Ignoring blob outside APT/SPECTR inbound or retry paths: {BlobPath}", normalizedPath);
            return;
        }

        if (!AptSpectrBlobPathHelper.TryParseSource(sourceKey, out var source))
        {
            _logger.LogWarning("Unknown APT/SPECTR source folder in blob path: {BlobPath}", normalizedPath);
            return;
        }

        var metadataKey = AptSpectrBlobPathHelper.ToMetadataKey(_resources.BlobContainerName, normalizedPath);
        var schema = source == AptSpectrSources.Apt ? AptSpectrSchemas.Apt : AptSpectrSchemas.Spectr;
        await _metadata.EnsureTablesAsync(cancellationToken);

        if (await _metadata.IsAlreadyProcessedAsync(schema, metadataKey, cancellationToken))
        {
            _logger.LogInformation("Blob already processed, moving to processed folder: {BlobPath}", normalizedPath);
            await MoveBlobAsync(sourceKey, stage, AptSpectrBlobFolders.Processed, fileName, null, cancellationToken);
            return;
        }

        var attemptNumber = await _blobStorage.GetRetryCountAsync(normalizedPath, cancellationToken) + 1;
        var maxAttempts = _resources.AptSpectrMaxRetryAttempts;
        var applyTransformations = source == AptSpectrSources.Apt
            ? _resources.AptApplyTransformations
            : _resources.SpectrApplyTransformations;

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
        try
        {
            var properties = await _blobStorage.GetPropertiesAsync(normalizedPath, cancellationToken);
            await _blobStorage.DownloadToFileAsync(normalizedPath, tempFile, cancellationToken);

            _logger.LogInformation(
                "Processing blob {BlobPath} source={Source} attempt={Attempt}/{MaxAttempts}",
                normalizedPath,
                source,
                attemptNumber,
                maxAttempts);

            var result = await _aptSpectrService.ProcessLocalFileAsync(
                source,
                metadataKey,
                fileName,
                tempFile,
                properties.ContentLength,
                properties.LastModified.UtcDateTime,
                applyTransformations,
                cancellationToken);

            if (result.Succeeded)
            {
                await MoveBlobAsync(sourceKey, stage, AptSpectrBlobFolders.Processed, fileName, null, cancellationToken);
                _logger.LogInformation("Blob processed successfully: {BlobPath}", normalizedPath);
                return;
            }

            await HandleFailureAsync(
                source,
                schema,
                metadataKey,
                sourceKey,
                stage,
                fileName,
                attemptNumber,
                maxAttempts,
                result.ErrorMessage ?? "Processing failed.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blob processing failed for {BlobPath}", normalizedPath);
            await HandleFailureAsync(
                source,
                schema,
                metadataKey,
                sourceKey,
                stage,
                fileName,
                attemptNumber,
                maxAttempts,
                ex.Message,
                cancellationToken);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task HandleFailureAsync(
        string source,
        string schema,
        string metadataKey,
        string sourceKey,
        string currentStage,
        string fileName,
        int attemptNumber,
        int maxAttempts,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var retriesExhausted = attemptNumber >= maxAttempts;

        if (retriesExhausted)
        {
            await _metadata.InsertMetadataAsync(new AptSpectrMetadataRecord
            {
                SchemaName = schema,
                Source = source,
                SourceFileName = fileName,
                SourceSharePath = metadataKey,
                FileSizeBytes = 0,
                Status = ProcessingStatus.Failed,
                ErrorMessage = $"Failed after {maxAttempts} attempts. Last error: {errorMessage}"
            }, cancellationToken);

            await MoveBlobAsync(sourceKey, currentStage, AptSpectrBlobFolders.Error, fileName, null, cancellationToken);
            _logger.LogError(
                "Blob moved to error folder after {MaxAttempts} attempts: {FileName}",
                maxAttempts,
                fileName);
            return;
        }

        var retryMetadata = new Dictionary<string, string>
        {
            [AptSpectrBlobFolders.RetryMetadataKey] = attemptNumber.ToString()
        };

        await MoveBlobAsync(
            sourceKey,
            currentStage,
            AptSpectrBlobFolders.Retry,
            fileName,
            retryMetadata,
            cancellationToken);

        _logger.LogWarning(
            "Blob moved to retry folder (attempt {Attempt}/{MaxAttempts}): {FileName}",
            attemptNumber,
            maxAttempts,
            fileName);
    }

    private async Task MoveBlobAsync(
        string sourceKey,
        string currentStage,
        string destinationStage,
        string fileName,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var sourcePath = AptSpectrBlobPathHelper.BuildPath(sourceKey, currentStage, fileName);
        var destinationPath = AptSpectrBlobPathHelper.BuildPath(sourceKey, destinationStage, fileName);

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _blobStorage.MoveAsync(sourcePath, destinationPath, metadata, cancellationToken);
    }

    private static bool TryParseBlobPath(
        string blobPath,
        out string sourceKey,
        out string stage,
        out string fileName)
    {
        sourceKey = "";
        stage = "";
        fileName = "";

        var parts = blobPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        sourceKey = parts[0];
        stage = parts[1];
        fileName = parts[2];

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return stage is AptSpectrBlobFolders.Inbound or AptSpectrBlobFolders.Retry;
    }
}
