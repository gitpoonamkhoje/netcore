using System.ComponentModel.DataAnnotations;

namespace PollProcessorFunction.Configuration;

public sealed class AppResourcesOptions
{
    public const string SectionName = "AppResources";

    [Required]
    public string SqlConnectionString { get; init; } = "";

    /// <summary>
    /// Storage connection string used by Azure Files (and often the Functions runtime).
    /// </summary>
    [Required]
    public string AzureWebJobsStorage { get; init; } = "";

    [Required]
    public string StorageAccountName { get; init; } = "";

    [Required]
    public string FileShareName { get; init; } = "";

    public string DataFactorySubscriptionId { get; init; } = "";

    public string DataFactoryResourceGroupName { get; init; } = "";

    public string DataFactoryName { get; init; } = "";

    public string AdfManagementApiVersion { get; init; } = "2018-06-01";

    public string AdfTriggerUrl { get; init; } = "";

    public string SqlAgentValidationConnectionString { get; init; } = "";

    public string AdfValidationConnectionString { get; init; } = "";

    public string KeyVaultUri { get; init; } = "";

    public string KeyVaultManagedIdentityClientId { get; init; } = "";

    /// <summary>
    /// Short environment code used for Key Vault secret prefix lookup (Dev, Cert, Prod).
    /// </summary>
    public string AppEnvironmentCode { get; init; } = "";
}

