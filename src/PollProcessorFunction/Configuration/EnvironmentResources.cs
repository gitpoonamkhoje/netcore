using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace PollProcessorFunction.Configuration;

public sealed class EnvironmentResources : IEnvironmentResources
{
    private readonly AppResourcesOptions _options;

    public EnvironmentResources(IOptions<AppResourcesOptions> options)
    {
        _options = options.Value;
    }

    public string SqlConnectionString => _options.SqlConnectionString;
    public string AzureWebJobsStorage => _options.AzureWebJobsStorage;
    public string StorageAccountName => _options.StorageAccountName;
    public string FileShareName => _options.FileShareName;

    public SqlConnection CreateSqlConnection() => new(SqlConnectionString);

    public ShareClient CreateFileShareClient() => new(AzureWebJobsStorage, FileShareName);
}

