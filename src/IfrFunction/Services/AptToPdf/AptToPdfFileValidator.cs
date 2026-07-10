namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfFileValidator
{
    AptToPdfValidationResult Validate(string fileName, string localFilePath);
}

public sealed class AptToPdfValidationResult
{
    public bool IsValid { get; init; }
    public string FileType { get; init; } = "Unknown";
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Validates supported APT input types from the architecture diagram (DOCX, XLSX, HTML, CSV, images, PDF, JRN, AFP).
/// </summary>
public sealed class AptToPdfFileValidator : IAptToPdfFileValidator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".xls", ".html", ".htm", ".csv", ".txt",
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif", ".pdf"
    };

    public AptToPdfValidationResult Validate(string fileName, string localFilePath)
    {
        if (!File.Exists(localFilePath))
        {
            return Invalid("File not found on local disk.");
        }

        var fileInfo = new FileInfo(localFilePath);
        if (fileInfo.Length == 0)
        {
            return Invalid("File is empty.");
        }

        var extension = Path.GetExtension(fileName);
        if (SupportedExtensions.Contains(extension))
        {
            return Valid(FormatType(extension));
        }

        if (IsLikelyJrn(fileName, localFilePath))
        {
            return Valid("JRN");
        }

        if (IsLikelyAfp(localFilePath))
        {
            return Valid("AFP");
        }

        if (string.IsNullOrEmpty(extension))
        {
            return Valid("APT");
        }

        return Invalid($"Unsupported file type '{extension}'.");
    }

    private static bool IsLikelyJrn(string fileName, string localFilePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.StartsWith("JR", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using var reader = new StreamReader(localFilePath);
        var firstLine = reader.ReadLine();
        return firstLine is not null &&
               (firstLine.Contains("$JOB:", StringComparison.OrdinalIgnoreCase) ||
                firstLine.TrimStart().StartsWith('1') && firstLine.Contains("JOB", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyAfp(string localFilePath)
    {
        Span<byte> header = stackalloc byte[5];
        using var stream = File.OpenRead(localFilePath);
        if (stream.Read(header) < 3)
        {
            return false;
        }

        return header[0] == 0x5A && header[1] == 0xD5 && header[2] == 0xCF;
    }

    private static string FormatType(string extension) =>
        extension.TrimStart('.').ToUpperInvariant();

    private static AptToPdfValidationResult Valid(string fileType) =>
        new() { IsValid = true, FileType = fileType };

    private static AptToPdfValidationResult Invalid(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}
