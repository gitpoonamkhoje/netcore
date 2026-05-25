using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;

namespace PollProcessorFunction.Configuration;

/// <summary>
/// Single DI entrypoint to resolve environment-specific resource settings
/// and create common clients.
/// </summary>
public interface IEnvironmentResources
{
    string SqlConnectionString { get; }
    string AzureWebJobsStorage { get; }
    string StorageAccountName { get; }
    string FileShareName { get; }

    SqlConnection CreateSqlConnection();
    ShareClient CreateFileShareClient();
}

