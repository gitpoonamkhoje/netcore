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

    // APT-to-PDF pipeline (architecture diagram)
    public bool AptToPdfEnabled { get; init; } = true;

    public string AptToPdfInputContainer { get; init; } = "input";

    public string AptToPdfProcessingContainer { get; init; } = "processing";

    public string AptToPdfOutputContainer { get; init; } = "output";

    public string AptToPdfArchiveContainer { get; init; } = "archive";

    public string AptToPdfFailedContainer { get; init; } = "failed";

    public string AptToPdfLogsContainer { get; init; } = "logs";

    public string AptToPdfJobName { get; init; } = "APT_TO_PDF";

    /// <summary>Checks JOB_SCHEDULE in SQL before running (diagram: DB-driven scheduler).</summary>
    public bool AptToPdfUseDatabaseScheduler { get; init; } = true;

    /// <summary>Timer checks schedule every 5 minutes; job runs when NextRunTime is due.</summary>
    public string AptToPdfSchedulerCron { get; init; } = "0 */5 * * * *";

    public int AptToPdfMaxRetries { get; init; } = 3;

    public bool AptToPdfMoveToFailedContainer { get; init; } = true;

    public bool AptToPdfEnableDeadLetter { get; init; } = true;

    public bool AptToPdfLogErrorDetails { get; init; } = true;

    /// <summary>Path to legacy APT-to-PDF .exe deployed with the function app (Windows).</summary>
    public string LegacyAptPdfExePath { get; init; } = "";

    /// <summary>Argument template; {input} and {output} are replaced with file paths.</summary>
    public string LegacyAptPdfExeArguments { get; init; } = "\"{input}\" \"{output}\"";

    public int LegacyAptPdfTimeoutSeconds { get; init; } = 300;

    public string KeyVaultUri { get; init; } = "";

    public string KeyVaultManagedIdentityClientId { get; init; } = "";

    /// <summary>
    /// Creates MTB_APT / MTB_SPECTR metadata tables when missing (dev/cert only recommended).
    /// </summary>
    public bool EnsureMetadataTables { get; init; } = true;
}
