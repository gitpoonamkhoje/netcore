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
    public bool EnsureMetadataTables => _options.EnsureMetadataTables;

    public SqlConnection CreateIfrConnection() => new(IfrSqlConnectionString);

    public ShareClient CreateFileShareClient() => new(AzureWebJobsStorage, FileShareName);

    public BlobContainerClient CreateBlobContainerClient() =>
        new BlobServiceClient(AzureWebJobsStorage).GetBlobContainerClient(BlobContainerName);
}
