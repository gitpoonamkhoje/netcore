using System.ComponentModel.DataAnnotations;

namespace TabLoadFunction.Configuration;

public sealed class AppResourcesOptions
{
    public const string SectionName = "AppResources";

    [Required]
    public string SqlConnectionString { get; init; } = "";

    [Required]
    public string AzureWebJobsStorage { get; init; } = "";

    [Required]
    public string StorageAccountName { get; init; } = "";

    [Required]
    public string FileShareName { get; init; } = "";

    public string FtpSharePath { get; init; } = "ftp";

    public string DdlSharePath { get; init; } = "ddl";

    public string BackupSharePath { get; init; } = "backup/tabload";

    public string UnzipSharePath { get; init; } = "unzip";

    public string KeyVaultUri { get; init; } = "";

    public string KeyVaultManagedIdentityClientId { get; init; } = "";

    public string AppEnvironmentCode { get; init; } = "";
}
