namespace IfrFunction.Models;

public static class AptSpectrSources
{
    public const string Apt = "APT";
    public const string Spectr = "SPECTR";
}

public static class AptSpectrSchemas
{
    public const string Apt = "MTB_APT";
    public const string Spectr = "MTB_SPECTR";
    public const string MetadataTable = "DocumentMetadata";
}

public static class ProcessingStatus
{
    public const string Complete = "C";
    public const string Failed = "F";
}

public static class AptSpectrBlobFolders
{
    public const string Inbound = "inbound";
    public const string Retry = "retry";
    public const string Error = "error";
    public const string Processed = "processed";
    public const string RetryMetadataKey = "ifrretrycount";
}

public static class AptSpectrBlobPathHelper
{
    public static string ToMetadataKey(string containerName, string blobPath) =>
        $"blob:{containerName}/{Normalize(blobPath)}";

    public static string BuildPath(string sourceFolder, string stageFolder, string fileName) =>
        $"{sourceFolder.ToLowerInvariant()}/{stageFolder}/{fileName}";

    public static bool TryParseSource(string sourceFolder, out string source)
    {
        source = sourceFolder.Trim().ToUpperInvariant();
        return source is AptSpectrSources.Apt or AptSpectrSources.Spectr;
    }

    public static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
