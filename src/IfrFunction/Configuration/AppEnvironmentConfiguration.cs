using Microsoft.Extensions.Configuration;

namespace IfrFunction.Configuration;

public static class AppEnvironmentConfiguration
{
    public static string ResolveEnvironmentCode(IConfiguration configuration, string hostingEnvironmentName)
    {
        var explicitCode = GetOptionalSetting(configuration, "AppEnvironmentCode");
        if (!string.IsNullOrWhiteSpace(explicitCode))
        {
            return explicitCode.Trim();
        }

        return hostingEnvironmentName switch
        {
            "Development" => "Dev",
            "Certification" => "Cert",
            "Production" => "Prod",
            _ => hostingEnvironmentName
        };
    }

    public static string? GetOptionalSetting(IConfiguration configuration, string name) =>
        configuration[name]
        ?? configuration[$"{AppResourcesOptions.SectionName}:{name}"];
}
