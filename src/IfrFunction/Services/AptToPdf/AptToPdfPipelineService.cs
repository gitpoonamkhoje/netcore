using System.Text.Json;
using Microsoft.Extensions.Logging;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfPipelineService
{
    Task<AptToPdfPipelineResult> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Diagram 10-step APT-to-PDF pipeline: input → processing → convert → output/archive → SQL logs.
/// </summary>
public sealed class AptToPdfPipelineService : IAptToPdfPipelineService
{
    private readonly IEnvironmentResources _resources;
    private readonly IAptToPdfBlobService _blobService;
    private readonly IAptToPdfFileValidator _validator;
    private readonly IAptToPdfConverter _converter;
    private readonly IAptToPdfRepository _repository;
    private readonly ILogger<AptToPdfPipelineService> _logger;

    public AptToPdfPipelineService(
        IEnvironmentResources resources,
        IAptToPdfBlobService blobService,
        IAptToPdfFileValidator validator,
        IAptToPdfConverter converter,
        IAptToPdfRepository repository,
        ILogger<AptToPdfPipelineService> logger)
    {
        _resources = resources;
        _blobService = blobService;
        _validator = validator;
        _converter = converter;
        _repository = repository;
        _logger = logger;
    }

    public async Task<AptToPdfPipelineResult> RunAsync(CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var fileResults = new List<AptToPdfFileResult>();

        if (!_resources.AptToPdfEnabled)
        {
            return new AptToPdfPipelineResult
            {
                Succeeded = false,
                ErrorMessage = "APT-to-PDF pipeline is disabled.",
                LogMessages = logs
            };
        }

        await _repository.EnsureTablesAsync(cancellationToken);
        await _blobService.EnsureContainersAsync(cancellationToken);
        await _repository.UpdateJobScheduleStatusAsync(
            _resources.AptToPdfJobName,
            AptToPdfStatuses.Running,
            nextRunTimeUtc: null,
            cancellationToken);

        try
        {
            var inputBlobs = await _blobService.ListBlobsAsync(_resources.AptToPdfInputContainer, cancellationToken);
            Log(logs, $"Step 1: found {inputBlobs.Count} blob(s) in {_resources.AptToPdfInputContainer}.");

            foreach (var blobName in inputBlobs)
            {
                var result = await ProcessSingleBlobAsync(blobName, logs, cancellationToken);
                fileResults.Add(result);
            }

            var succeeded = fileResults.Count(f => f.Succeeded);
            var failed = fileResults.Count - succeeded;
            var allSucceeded = failed == 0;

            await WriteRunLogAsync(
                $"Run complete. Total={fileResults.Count}, Succeeded={succeeded}, Failed={failed}",
                cancellationToken);

            Log(logs, $"Pipeline finished. Succeeded={succeeded}, Failed={failed}.");

            return new AptToPdfPipelineResult
            {
                Succeeded = allSucceeded,
                TotalFiles = fileResults.Count,
                SucceededFiles = succeeded,
                FailedFiles = failed,
                Files = fileResults,
                LogMessages = logs,
                ErrorMessage = allSucceeded ? null : "One or more files failed."
            };
        }
        finally
        {
            var nextRun = CalculateNextRunUtc(DateTime.UtcNow);
            await _repository.UpdateJobScheduleStatusAsync(
                _resources.AptToPdfJobName,
                AptToPdfStatuses.Idle,
                nextRun,
                cancellationToken);
        }
    }

    private async Task<AptToPdfFileResult> ProcessSingleBlobAsync(
        string blobName,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var maxRetries = _resources.AptToPdfMaxRetries;
        var attempt = 0;
        string? lastError = null;

        while (attempt < maxRetries)
        {
            attempt++;
            var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{Path.GetFileName(blobName)}");
            var tempPdf = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
            var processingBlobName = blobName;

            try
            {
                Log(logs, $"[{blobName}] attempt {attempt}/{maxRetries}: move to processing.");
                await _blobService.MoveAsync(
                    _resources.AptToPdfInputContainer,
                    blobName,
                    _resources.AptToPdfProcessingContainer,
                    processingBlobName,
                    metadata: null,
                    cancellationToken);

                Log(logs, $"[{blobName}] Step 2: download locally.");
                await _blobService.DownloadAsync(
                    _resources.AptToPdfProcessingContainer,
                    processingBlobName,
                    tempInput,
                    cancellationToken);

                Log(logs, $"[{blobName}] Step 3: validate file.");
                var validation = _validator.Validate(blobName, tempInput);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(validation.ErrorMessage ?? "Validation failed.");
                }

                Log(logs, $"[{blobName}] Step 4: convert to PDF ({validation.FileType}).");
                await _converter.ConvertAsync(tempInput, tempPdf, validation.FileType, cancellationToken);

                var outputBlobName = $"{Path.GetFileNameWithoutExtension(blobName)}.pdf";
                Log(logs, $"[{blobName}] Step 5: upload PDF to output.");
                await _blobService.UploadFileAsync(
                    _resources.AptToPdfOutputContainer,
                    outputBlobName,
                    tempPdf,
                    cancellationToken);

                Log(logs, $"[{blobName}] Step 6: move original to archive.");
                await _blobService.MoveAsync(
                    _resources.AptToPdfProcessingContainer,
                    processingBlobName,
                    _resources.AptToPdfArchiveContainer,
                    blobName,
                    metadata: null,
                    cancellationToken);

                var properties = new FileInfo(tempInput);
                Log(logs, $"[{blobName}] Step 7-8: log success and update SQL metadata.");
                await _repository.InsertProcessLogAsync(new AptToPdfProcessLogRecord
                {
                    JobName = _resources.AptToPdfJobName,
                    FileName = blobName,
                    Status = AptToPdfStatuses.Complete,
                    Attempts = attempt,
                    OutputBlobName = outputBlobName,
                    Message = "Converted successfully."
                }, cancellationToken);

                await _repository.InsertFileMetadataAsync(new AptToPdfFileMetadataRecord
                {
                    SourceFileName = blobName,
                    SourceBlobPath = $"{_resources.AptToPdfInputContainer}/{blobName}",
                    OutputBlobName = outputBlobName,
                    ArchiveBlobName = blobName,
                    FileType = validation.FileType,
                    FileSizeBytes = properties.Length,
                    Status = ProcessingStatus.Complete
                }, cancellationToken);

                Log(logs, $"[{blobName}] Step 9: delete working files.");
                return new AptToPdfFileResult
                {
                    FileName = blobName,
                    Succeeded = true,
                    Attempts = attempt,
                    OutputBlobName = outputBlobName
                };
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                _logger.LogError(ex, "APT-to-PDF failed for {Blob} on attempt {Attempt}", blobName, attempt);

                if (attempt >= maxRetries)
                {
                    await HandleFinalFailureAsync(blobName, processingBlobName, attempt, ex, cancellationToken);
                    Log(logs, $"[{blobName}] Step 10: moved to failed after {attempt} attempts.");
                    return new AptToPdfFileResult
                    {
                        FileName = blobName,
                        Succeeded = false,
                        Attempts = attempt,
                        ErrorMessage = lastError
                    };
                }

                Log(logs, $"[{blobName}] attempt {attempt} failed: {ex.Message}. Retrying.");
                try
                {
                    await _blobService.MoveAsync(
                        _resources.AptToPdfProcessingContainer,
                        processingBlobName,
                        _resources.AptToPdfInputContainer,
                        blobName,
                        new Dictionary<string, string>
                        {
                            [AptToPdfBlobMetadata.RetryCount] = attempt.ToString()
                        },
                        cancellationToken);
                }
                catch (Exception moveEx)
                {
                    _logger.LogError(moveEx, "Failed to move blob back to input for retry: {Blob}", blobName);
                    return new AptToPdfFileResult
                    {
                        FileName = blobName,
                        Succeeded = false,
                        Attempts = attempt,
                        ErrorMessage = moveEx.Message
                    };
                }
            }
            finally
            {
                DeleteIfExists(tempInput);
                DeleteIfExists(tempPdf);
            }
        }

        return new AptToPdfFileResult
        {
            FileName = blobName,
            Succeeded = false,
            Attempts = attempt,
            ErrorMessage = lastError
        };
    }

    private async Task HandleFinalFailureAsync(
        string blobName,
        string processingBlobName,
        int attempts,
        Exception ex,
        CancellationToken cancellationToken)
    {
        if (_resources.AptToPdfLogErrorDetails)
        {
            await _repository.InsertErrorLogAsync(new AptToPdfErrorLogRecord
            {
                JobName = _resources.AptToPdfJobName,
                FileName = blobName,
                Attempts = attempts,
                ErrorMessage = ex.Message,
                Details = ex.ToString()
            }, cancellationToken);

            await _repository.InsertProcessLogAsync(new AptToPdfProcessLogRecord
            {
                JobName = _resources.AptToPdfJobName,
                FileName = blobName,
                Status = AptToPdfStatuses.Failed,
                Attempts = attempts,
                Message = ex.Message
            }, cancellationToken);
        }

        if (_resources.AptToPdfMoveToFailedContainer)
        {
            await _blobService.MoveAsync(
                _resources.AptToPdfProcessingContainer,
                processingBlobName,
                _resources.AptToPdfFailedContainer,
                blobName,
                metadata: null,
                cancellationToken);

            if (_resources.AptToPdfEnableDeadLetter)
            {
                var errorJson = JsonSerializer.Serialize(new
                {
                    fileName = blobName,
                    attempts,
                    error = ex.Message,
                    timestampUtc = DateTime.UtcNow
                });

                await _blobService.UploadTextAsync(
                    _resources.AptToPdfFailedContainer,
                    $"{blobName}.error.json",
                    errorJson,
                    cancellationToken);
            }
        }
    }

    private async Task WriteRunLogAsync(string message, CancellationToken cancellationToken)
    {
        var logBlobName = $"function-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";
        await _blobService.UploadTextAsync(_resources.AptToPdfLogsContainer, logBlobName, message, cancellationToken);
    }

    private static DateTime CalculateNextRunUtc(DateTime fromUtc)
    {
        var next = fromUtc.Date.AddDays(1).AddHours(20);
        if (next <= fromUtc)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void Log(List<string> logs, string message) =>
        logs.Add($"{DateTime.UtcNow:O} {message}");
}
