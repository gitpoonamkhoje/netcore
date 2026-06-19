using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace TabLoadFunction.Configuration;

public sealed class PrefixedKeyVaultSecretManager : KeyVaultSecretManager
{
    private readonly string _prefix;

    public PrefixedKeyVaultSecretManager(string environmentCode)
    {
        if (string.IsNullOrWhiteSpace(environmentCode))
        {
            throw new ArgumentException("Environment code is required.", nameof(environmentCode));
        }

        _prefix = $"{environmentCode.Trim()}--";
    }

    public override bool Load(SecretProperties secret) =>
        secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);

    public override string GetKey(KeyVaultSecret secret)
    {
        var suffix = secret.Name[_prefix.Length..];
        return suffix.Replace("--", ConfigurationPath.KeyDelimiter);
    }
}
