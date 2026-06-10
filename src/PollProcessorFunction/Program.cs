using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Services;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        var env = context.HostingEnvironment.EnvironmentName;
        if (!string.IsNullOrWhiteSpace(env))
        {
            config.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false);
        }

        config.AddEnvironmentVariables();

        var initialConfiguration = config.Build();
        var keyVaultUri = initialConfiguration["KeyVaultUri"]
            ?? initialConfiguration["AppResources:KeyVaultUri"];

        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            var managedIdentityClientId = initialConfiguration["KeyVaultManagedIdentityClientId"]
                ?? initialConfiguration["AppResources:KeyVaultManagedIdentityClientId"];

            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
            {
                credentialOptions.ManagedIdentityClientId = managedIdentityClientId;
            }

            config.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential(credentialOptions),
                new AzureKeyVaultConfigurationOptions());

            // Keep Azure Portal app settings as final override for emergency/env-specific changes.
            config.AddEnvironmentVariables();
        }
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AppResourcesOptions>()
            .Bind(context.Configuration.GetSection(AppResourcesOptions.SectionName));

        services.AddSingleton<IEnvironmentResources, EnvironmentResources>();
        services.AddSingleton<IPollProcessRepository, PollProcessRepository>();
        services.AddSingleton<IFileReadyChecker, FileReadyChecker>();
        services.AddSingleton<IAdfPipelineTriggerService, AdfPipelineTriggerService>();
        services.AddSingleton<IAdfPipelineMetadataService, AdfPipelineMetadataService>();
        services.AddSingleton<IAdfPipelineInfoSqlWriter, AdfPipelineInfoSqlWriter>();
        services.AddSingleton<IAdfPipelineRuntimeSqlWriter, AdfPipelineRuntimeSqlWriter>();
        services.AddSingleton<IPollNotificationService, PollNotificationService>();

        services.AddHttpClient(nameof(AdfPipelineTriggerService));
        services.AddHttpClient(nameof(AdfPipelineMetadataService));
    })
    .Build();

host.Run();
