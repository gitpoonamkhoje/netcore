using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfConverter
{
    Task ConvertAsync(string inputPath, string outputPdfPath, string fileType, CancellationToken cancellationToken);
}

/// <summary>
/// Uses legacy APT-to-PDF .exe when configured; otherwise falls back to built-in PDFSharp converter.
/// </summary>
public sealed class AptToPdfConverter : IAptToPdfConverter
{
    private readonly Configuration.IEnvironmentResources _resources;
    private readonly IAptSpectrPdfConverter _fallbackConverter;
    private readonly ILogger<AptToPdfConverter> _logger;

    public AptToPdfConverter(
        Configuration.IEnvironmentResources resources,
        IAptSpectrPdfConverter fallbackConverter,
        ILogger<AptToPdfConverter> logger)
    {
        _resources = resources;
        _fallbackConverter = fallbackConverter;
        _logger = logger;
    }

    public async Task ConvertAsync(
        string inputPath,
        string outputPdfPath,
        string fileType,
        CancellationToken cancellationToken)
    {
        var legacyExe = _resources.LegacyAptPdfExePath?.Trim();
        if (!string.IsNullOrWhiteSpace(legacyExe) && File.Exists(legacyExe))
        {
            await RunLegacyConverterAsync(legacyExe, inputPath, outputPdfPath, cancellationToken);
            return;
        }

        if (fileType is "DOCX" or "XLSX" or "XLS" or "HTML" or "HTM" or "CSV")
        {
            throw new NotSupportedException(
                $"File type {fileType} requires the legacy APT-to-PDF executable. Set AppResources:LegacyAptPdfExePath.");
        }

        _logger.LogInformation("Using built-in PDF converter for file type {FileType}.", fileType);
        await _fallbackConverter.ConvertToPdfAsync(inputPath, outputPdfPath, applyTransformations: false, cancellationToken);
    }

    private async Task RunLegacyConverterAsync(
        string exePath,
        string inputPath,
        string outputPdfPath,
        CancellationToken cancellationToken)
    {
        var arguments = _resources.LegacyAptPdfExeArguments
            .Replace("{input}", inputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{output}", outputPdfPath, StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Running legacy APT converter: {Exe} {Args}", exePath, arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_resources.LegacyAptPdfTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Legacy APT converter timed out after {_resources.LegacyAptPdfTimeoutSeconds} seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Legacy APT converter failed with exit code {process.ExitCode}. stderr: {stderr} stdout: {stdout}");
        }

        if (!File.Exists(outputPdfPath))
        {
            throw new FileNotFoundException("Legacy APT converter did not produce an output PDF.", outputPdfPath);
        }
    }
}
