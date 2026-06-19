using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace TabLoadFunction.Configuration;

public sealed class EnvironmentResources : IEnvironmentResources
{
    private readonly AppResourcesOptions _options;

    public EnvironmentResources(IOptions<AppResourcesOptions> options)
    {
        _options = options.Value;
    }

    public string SqlConnectionString => _options.SqlConnectionString;
    public string AzureWebJobsStorage => _options.AzureWebJobsStorage;
    public string FileShareName => _options.FileShareName;
    public string FtpSharePath => _options.FtpSharePath;
    public string DdlSharePath => _options.DdlSharePath;
    public string BackupSharePath => _options.BackupSharePath;
    public string UnzipSharePath => _options.UnzipSharePath;

    public SqlConnection CreateSqlConnection() => new(SqlConnectionString);

    public ShareClient CreateFileShareClient() => new(AzureWebJobsStorage, FileShareName);
}
