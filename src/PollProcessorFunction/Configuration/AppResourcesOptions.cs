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
}

