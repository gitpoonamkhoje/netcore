using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;

namespace IfrFunction.Configuration;

public interface IEnvironmentResources
{
    string IfrSqlConnectionString { get; }
    string AzureWebJobsStorage { get; }
    string FileShareName { get; }
    string AptInputSharePath { get; }
    string SpectrInputSharePath { get; }
    string ArchiveSharePath { get; }
    string AptProcessedSharePath { get; }
    string SpectrProcessedSharePath { get; }
    string LogoSharePath { get; }
    bool AptSpectrPollingEnabled { get; }
    string AptSpectrPollCron { get; }
    bool MoveProcessedFiles { get; }
    bool AptApplyTransformations { get; }
    bool SpectrApplyTransformations { get; }
    string BlobContainerName { get; }
    int AptSpectrMaxRetryAttempts { get; }
    bool AptToPdfEnabled { get; }
    string AptToPdfInputContainer { get; }
    string AptToPdfProcessingContainer { get; }
    string AptToPdfOutputContainer { get; }
    string AptToPdfArchiveContainer { get; }
    string AptToPdfFailedContainer { get; }
    string AptToPdfLogsContainer { get; }
    string AptToPdfJobName { get; }
    bool AptToPdfUseDatabaseScheduler { get; }
    string AptToPdfSchedulerCron { get; }
    int AptToPdfMaxRetries { get; }
    bool AptToPdfMoveToFailedContainer { get; }
    bool AptToPdfEnableDeadLetter { get; }
    bool AptToPdfLogErrorDetails { get; }
    string LegacyAptPdfExePath { get; }
    string LegacyAptPdfExeArguments { get; }
    int LegacyAptPdfTimeoutSeconds { get; }
    bool EnsureMetadataTables { get; }

    SqlConnection CreateIfrConnection();
    ShareClient CreateFileShareClient();
    Azure.Storage.Blobs.BlobContainerClient CreateBlobContainerClient(string containerName);
    Azure.Storage.Blobs.BlobServiceClient CreateBlobServiceClient();
}
