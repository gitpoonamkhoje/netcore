using Azure.Storage.Files.Shares;
using Microsoft.Data.SqlClient;

namespace TabLoadFunction.Configuration;

public interface IEnvironmentResources
{
    string SqlConnectionString { get; }
    string AzureWebJobsStorage { get; }
    string FileShareName { get; }
    string FtpSharePath { get; }
    string DdlSharePath { get; }
    string BackupSharePath { get; }
    string UnzipSharePath { get; }

    SqlConnection CreateSqlConnection();
    ShareClient CreateFileShareClient();
}
