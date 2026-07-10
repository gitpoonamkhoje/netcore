using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace IfrFunction.Configuration;

public sealed class EnvironmentResources : IEnvironmentResources
{
    private readonly AppResourcesOptions _options;

    public EnvironmentResources(IOptions<AppResourcesOptions> options)
    {
        _options = options.Value;
    }

    public string IfrSqlConnectionString => _options.IfrSqlConnectionString;
    public string AzureWebJobsStorage => _options.AzureWebJobsStorage;
    public string FileShareName => _options.FileShareName;
    public string AptInputSharePath => _options.AptInputSharePath;
    public string SpectrInputSharePath => _options.SpectrInputSharePath;
    public string ArchiveSharePath => _options.ArchiveSharePath;
    public string AptProcessedSharePath => _options.AptProcessedSharePath;
    public string SpectrProcessedSharePath => _options.SpectrProcessedSharePath;
    public string LogoSharePath => _options.LogoSharePath;
    public bool AptSpectrPollingEnabled => _options.AptSpectrPollingEnabled;
    public string AptSpectrPollCron => _options.AptSpectrPollCron;
    public bool MoveProcessedFiles => _options.MoveProcessedFiles;
    public bool AptApplyTransformations => _options.AptApplyTransformations;
    public bool SpectrApplyTransformations => _options.SpectrApplyTransformations;
    public string BlobContainerName => _options.BlobContainerName;
    public int AptSpectrMaxRetryAttempts => _options.AptSpectrMaxRetryAttempts;
    public bool AptToPdfEnabled => _options.AptToPdfEnabled;
    public string AptToPdfInputContainer => _options.AptToPdfInputContainer;
    public string AptToPdfProcessingContainer => _options.AptToPdfProcessingContainer;
    public string AptToPdfOutputContainer => _options.AptToPdfOutputContainer;
    public string AptToPdfArchiveContainer => _options.AptToPdfArchiveContainer;
    public string AptToPdfFailedContainer => _options.AptToPdfFailedContainer;
    public string AptToPdfLogsContainer => _options.AptToPdfLogsContainer;
    public string AptToPdfJobName => _options.AptToPdfJobName;
    public bool AptToPdfUseDatabaseScheduler => _options.AptToPdfUseDatabaseScheduler;
    public string AptToPdfSchedulerCron => _options.AptToPdfSchedulerCron;
    public int AptToPdfMaxRetries => _options.AptToPdfMaxRetries;
    public bool AptToPdfMoveToFailedContainer => _options.AptToPdfMoveToFailedContainer;
    public bool AptToPdfEnableDeadLetter => _options.AptToPdfEnableDeadLetter;
    public bool AptToPdfLogErrorDetails => _options.AptToPdfLogErrorDetails;
    public string LegacyAptPdfExePath => _options.LegacyAptPdfExePath;
    public string LegacyAptPdfExeArguments => _options.LegacyAptPdfExeArguments;
    public int LegacyAptPdfTimeoutSeconds => _options.LegacyAptPdfTimeoutSeconds;
    public bool EnsureMetadataTables => _options.EnsureMetadataTables;

    public SqlConnection CreateIfrConnection() => new(IfrSqlConnectionString);

    public ShareClient CreateFileShareClient() => new(AzureWebJobsStorage, FileShareName);

    public BlobServiceClient CreateBlobServiceClient() => new(AzureWebJobsStorage);

    public BlobContainerClient CreateBlobContainerClient(string containerName) =>
        CreateBlobServiceClient().GetBlobContainerClient(containerName);

    public BlobContainerClient CreateBlobContainerClient() =>
        CreateBlobContainerClient(_options.BlobContainerName);
}
