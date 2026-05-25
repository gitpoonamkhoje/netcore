using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Services;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddEnvironmentVariables();
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        var env = context.HostingEnvironment.EnvironmentName;
        if (!string.IsNullOrWhiteSpace(env))
        {
            config.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false);
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
        services.AddSingleton<IPollNotificationService, PollNotificationService>();

        services.AddHttpClient(nameof(AdfPipelineTriggerService));
    })
    .Build();

host.Run();
