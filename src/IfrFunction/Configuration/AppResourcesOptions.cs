using System.ComponentModel.DataAnnotations;

namespace IfrFunction.Configuration;

public sealed class AppResourcesOptions
{
    public const string SectionName = "AppResources";

    public string AppEnvironmentCode { get; init; } = "";

    [Required]
    public string IfrSqlConnectionString { get; init; } = "";

    [Required]
    public string AzureWebJobsStorage { get; init; } = "";

    [Required]
    public string FileShareName { get; init; } = "";

    public string AptInputSharePath { get; init; } = "ifr/apt/inbound";

    public string SpectrInputSharePath { get; init; } = "ifr/spectr/inbound";

    public string ArchiveSharePath { get; init; } = "ifr/archive";

    public string AptProcessedSharePath { get; init; } = "ifr/apt/processed";

    public string SpectrProcessedSharePath { get; init; } = "ifr/spectr/processed";

    /// <summary>Logo image on file share applied when transformations are enabled (SPECTR path).</summary>
    public string LogoSharePath { get; init; } = "ifr/assets/logo.png";

    public bool AptSpectrPollingEnabled { get; init; }

    /// <summary>NCRONTAB expression, e.g. every 15 minutes: 0 */15 * * * *</summary>
    public string AptSpectrPollCron { get; init; } = "0 */15 * * * *";

    public bool MoveProcessedFiles { get; init; } = true;

    public bool AptApplyTransformations { get; init; }

    public bool SpectrApplyTransformations { get; init; } = true;

    /// <summary>Blob container for event-driven APT/SPECTR intake (e.g. ifr).</summary>
    public string BlobContainerName { get; init; } = "ifr";

    /// <summary>Max processing attempts before a blob is moved to the error folder.</summary>
    public int AptSpectrMaxRetryAttempts { get; init; } = 3;

    public string KeyVaultUri { get; init; } = "";

    public string KeyVaultManagedIdentityClientId { get; init; } = "";

    /// <summary>
    /// Creates MTB_APT / MTB_SPECTR metadata tables when missing (dev/cert only recommended).
    /// </summary>
    public bool EnsureMetadataTables { get; init; } = true;
}
