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
    bool EnsureMetadataTables { get; }

    SqlConnection CreateIfrConnection();
    ShareClient CreateFileShareClient();
    Azure.Storage.Blobs.BlobContainerClient CreateBlobContainerClient();
}
