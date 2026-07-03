using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IfrFunction.Configuration;
using IfrFunction.Services;

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
        var keyVaultUri = AppEnvironmentConfiguration.GetOptionalSetting(initialConfiguration, "KeyVaultUri");

        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            var managedIdentityClientId = AppEnvironmentConfiguration.GetOptionalSetting(
                initialConfiguration,
                "KeyVaultManagedIdentityClientId");
            var environmentCode = AppEnvironmentConfiguration.ResolveEnvironmentCode(
                initialConfiguration,
                context.HostingEnvironment.EnvironmentName);

            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
            {
                credentialOptions.ManagedIdentityClientId = managedIdentityClientId;
            }

            config.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential(credentialOptions),
                new PrefixedKeyVaultSecretManager(environmentCode));

            config.AddEnvironmentVariables();
        }
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<AppResourcesOptions>()
            .Bind(context.Configuration.GetSection(AppResourcesOptions.SectionName));

        services.AddSingleton<IEnvironmentResources, EnvironmentResources>();
        services.AddSingleton<IFileShareService, FileShareService>();
        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddSingleton<IAptSpectrPdfConverter, AptSpectrPdfConverter>();
        services.AddSingleton<IAptSpectrMetadataRepository, AptSpectrMetadataRepository>();
        services.AddSingleton<IAptSpectrService, AptSpectrService>();
        services.AddSingleton<IAptSpectrBlobProcessor, AptSpectrBlobProcessor>();
    })
    .Build();

host.Run();
