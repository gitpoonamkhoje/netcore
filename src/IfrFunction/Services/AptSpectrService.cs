using Microsoft.Extensions.Logging;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services;

public interface IAptSpectrService
{
    Task<AptSpectrResult> ProcessAsync(AptSpectrRequest request, CancellationToken cancellationToken);

    Task<AptSpectrFileResult> ProcessLocalFileAsync(
        string source,
        string sourceLocationKey,
        string fileName,
        string localFilePath,
        long fileSizeBytes,
        DateTime? sourceModifiedUtc,
        bool applyTransformations,
        CancellationToken cancellationToken);
}

public sealed class AptSpectrService : IAptSpectrService
{
    private readonly IEnvironmentResources _resources;
    private readonly IFileShareService _fileShare;
    private readonly IAptSpectrPdfConverter _pdfConverter;
    private readonly IAptSpectrMetadataRepository _metadata;
    private readonly ILogger<AptSpectrService> _logger;

    public AptSpectrService(
        IEnvironmentResources resources,
        IFileShareService fileShare,
        IAptSpectrPdfConverter pdfConverter,
        IAptSpectrMetadataRepository metadata,
        ILogger<AptSpectrService> logger)
    {
        _resources = resources;
        _fileShare = fileShare;
        _pdfConverter = pdfConverter;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task<AptSpectrResult> ProcessAsync(AptSpectrRequest request, CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var source = NormalizeSource(request.Source);
        var schema = ResolveSchema(source);
        var paths = await ResolveInputPathsAsync(request, source, cancellationToken);
        if (paths.Count == 0)
        {
            return new AptSpectrResult
            {
                Succeeded = false,
                Source = source,
                ErrorMessage = "No input files found. Provide InputSharePath or InputDirectory.",
                LogMessages = logs
            };
        }

        await _metadata.EnsureTablesAsync(cancellationToken);

        var moveProcessed = request.MoveToProcessedFolder ?? _resources.MoveProcessedFiles;
        var processedFolder = ResolveProcessedFolder(source);

        var fileResults = new List<AptSpectrFileResult>();
        foreach (var sharePath in paths)
        {
            if (request.SkipAlreadyProcessed &&
                await _metadata.IsAlreadyProcessedAsync(schema, sharePath, cancellationToken))
            {
                Log(logs, $"{source}: skipped already processed {sharePath}");
                fileResults.Add(new AptSpectrFileResult
                {
                    Source = source,
                    SourceFileName = Path.GetFileName(sharePath),
                    SourceSharePath = sharePath,
                    Succeeded = true,
                    Skipped = true
                });
                continue;
            }

            var fileResult = await ProcessSingleFileAsync(
                source,
                schema,
                sharePath,
                request.ApplyTransformations,
                moveProcessed,
                processedFolder,
                logs,
                cancellationToken);
            fileResults.Add(fileResult);
        }

        var processedCount = fileResults.Count(f => !f.Skipped);
        var allSucceeded = fileResults.All(f => f.Succeeded);
        Log(logs, $"{source}: processed {processedCount} file(s), skipped {fileResults.Count(f => f.Skipped)}, success={allSucceeded}");

        return new AptSpectrResult
        {
            Succeeded = allSucceeded,
            Source = source,
            Files = fileResults,
            LogMessages = logs,
            ErrorMessage = allSucceeded ? null : "One or more files failed."
        };
    }

    public async Task<AptSpectrFileResult> ProcessLocalFileAsync(
        string source,
        string sourceLocationKey,
        string fileName,
        string localFilePath,
        long fileSizeBytes,
        DateTime? sourceModifiedUtc,
        bool applyTransformations,
        CancellationToken cancellationToken)
    {
        var normalizedSource = NormalizeSource(source);
        var schema = ResolveSchema(normalizedSource);
        var logs = new List<string>();

        await _metadata.EnsureTablesAsync(cancellationToken);

        return await ProcessLocalFileCoreAsync(
            normalizedSource,
            schema,
            sourceLocationKey,
            fileName,
            localFilePath,
            fileSizeBytes,
            sourceModifiedUtc,
            applyTransformations,
            moveShareSourcePath: null,
            processedFolder: null,
            logs,
            cancellationToken);
    }

    private async Task<AptSpectrFileResult> ProcessSingleFileAsync(
        string source,
        string schema,
        string sourceSharePath,
        bool applyTransformations,
        bool moveProcessed,
        string processedFolder,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(sourceSharePath);
        var tempSource = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
        var tempPdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");

        try
        {
            if (!await _fileShare.ExistsAsync(sourceSharePath, cancellationToken))
            {
                throw new FileNotFoundException($"Input file not found on share: {sourceSharePath}");
            }

            var properties = await _fileShare.GetPropertiesAsync(sourceSharePath, cancellationToken);
            await _fileShare.DownloadToFileAsync(sourceSharePath, tempSource, cancellationToken);
            Log(logs, $"{source}: downloaded {sourceSharePath}");

            return await ProcessLocalFileCoreAsync(
                source,
                schema,
                sourceSharePath,
                fileName,
                tempSource,
                properties.ContentLength,
                properties.LastModified.UtcDateTime,
                applyTransformations,
                moveShareSourcePath: moveProcessed ? sourceSharePath : null,
                processedFolder: moveProcessed ? processedFolder : null,
                logs,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "APT/SPECTR failed for {Path}", sourceSharePath);
            var failedResult = new AptSpectrFileResult
            {
                Source = source,
                SourceFileName = fileName,
                SourceSharePath = sourceSharePath,
                Succeeded = false,
                ErrorMessage = ex.Message
            };

            try
            {
                await _metadata.InsertMetadataAsync(new AptSpectrMetadataRecord
                {
                    SchemaName = schema,
                    Source = source,
                    SourceFileName = fileName,
                    SourceSharePath = sourceSharePath,
                    FileSizeBytes = 0,
                    Status = ProcessingStatus.Failed,
                    ErrorMessage = ex.Message
                }, cancellationToken);
            }
            catch (Exception metaEx)
            {
                _logger.LogError(metaEx, "Failed to write failure metadata for {Path}", sourceSharePath);
            }

            Log(logs, $"{source}: FAILED {fileName} — {ex.Message}");
            return failedResult;
        }
        finally
        {
            if (File.Exists(tempSource))
            {
                File.Delete(tempSource);
            }

            if (File.Exists(tempPdf))
            {
                File.Delete(tempPdf);
            }
        }
    }

    private async Task<AptSpectrFileResult> ProcessLocalFileCoreAsync(
        string source,
        string schema,
        string sourceLocationKey,
        string fileName,
        string localFilePath,
        long fileSizeBytes,
        DateTime? sourceModifiedUtc,
        bool applyTransformations,
        string? moveShareSourcePath,
        string? processedFolder,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var result = new AptSpectrFileResult
        {
            Source = source,
            SourceFileName = fileName,
            SourceSharePath = sourceLocationKey
        };

        var tempPdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");

        try
        {
            await _pdfConverter.ConvertToPdfAsync(localFilePath, tempPdf, applyTransformations, cancellationToken);
            var pdfFileName = Path.ChangeExtension(fileName, ".pdf");
            var archiveSharePath = $"{_resources.ArchiveSharePath}/{source.ToLowerInvariant()}/{pdfFileName}";

            await _fileShare.UploadFromFileAsync(archiveSharePath, tempPdf, cancellationToken);
            Log(logs, $"{source}: archived PDF to {archiveSharePath}");

            var metadataId = await _metadata.InsertMetadataAsync(new AptSpectrMetadataRecord
            {
                SchemaName = schema,
                Source = source,
                SourceFileName = fileName,
                SourceSharePath = sourceLocationKey,
                PdfFileName = pdfFileName,
                PdfSharePath = archiveSharePath,
                ArchiveSharePath = archiveSharePath,
                FileSizeBytes = fileSizeBytes,
                SourceModifiedUtc = sourceModifiedUtc,
                Status = ProcessingStatus.Complete
            }, cancellationToken);

            result.Succeeded = true;
            result.PdfSharePath = archiveSharePath;
            result.ArchiveSharePath = archiveSharePath;
            result.MetadataId = metadataId;
            Log(logs, $"{source}: metadata Id={metadataId} in {schema}.{AptSpectrSchemas.MetadataTable}");

            if (moveShareSourcePath is not null && processedFolder is not null)
            {
                var processedPath = $"{processedFolder}/{fileName}";
                await _fileShare.MoveAsync(moveShareSourcePath, processedPath, cancellationToken);
                Log(logs, $"{source}: moved source to {processedPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "APT/SPECTR failed for {Path}", sourceLocationKey);
            result.Succeeded = false;
            result.ErrorMessage = ex.Message;
            Log(logs, $"{source}: FAILED {fileName} — {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempPdf))
            {
                File.Delete(tempPdf);
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<string>> ResolveInputPathsAsync(
        AptSpectrRequest request,
        string source,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.InputSharePath))
        {
            return new[] { NormalizePath(request.InputSharePath) };
        }

        var directory = !string.IsNullOrWhiteSpace(request.InputDirectory)
            ? NormalizePath(request.InputDirectory)
            : NormalizePath(source == AptSpectrSources.Apt
                ? _resources.AptInputSharePath
                : _resources.SpectrInputSharePath);

        return await _fileShare.ListFilesAsync(directory, cancellationToken);
    }

    private string ResolveProcessedFolder(string source) =>
        NormalizePath(source == AptSpectrSources.Apt
            ? _resources.AptProcessedSharePath
            : _resources.SpectrProcessedSharePath);

    private static string NormalizeSource(string source)
    {
        var normalized = source.Trim().ToUpperInvariant();
        return normalized switch
        {
            AptSpectrSources.Apt => AptSpectrSources.Apt,
            AptSpectrSources.Spectr => AptSpectrSources.Spectr,
            _ => throw new ArgumentException($"Source must be {AptSpectrSources.Apt} or {AptSpectrSources.Spectr}.")
        };
    }

    private static string ResolveSchema(string source) =>
        source == AptSpectrSources.Apt ? AptSpectrSchemas.Apt : AptSpectrSchemas.Spectr;

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static void Log(List<string> logs, string message) =>
        logs.Add($"{DateTime.UtcNow:O} {message}");
}
