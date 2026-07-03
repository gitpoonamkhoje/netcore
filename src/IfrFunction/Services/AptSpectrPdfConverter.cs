using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using IfrFunction.Configuration;

namespace IfrFunction.Services;

public interface IAptSpectrPdfConverter
{
    Task<string> ConvertToPdfAsync(
        string sourceFilePath,
        string outputPdfPath,
        bool applyTransformations,
        CancellationToken cancellationToken);
}

/// <summary>
/// Diagram: as-is conversion using PDFSharp. SPECTR path supports logo, **bold**, and __underline__ markup.
/// </summary>
public sealed class AptSpectrPdfConverter : IAptSpectrPdfConverter
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif"
    };

    private const double Margin = 40;
    private const double LineHeight = 14;
    private const double LogoMaxHeight = 60;

    private readonly IEnvironmentResources _resources;
    private readonly IFileShareService _fileShare;
    private readonly ILogger<AptSpectrPdfConverter> _logger;

    public AptSpectrPdfConverter(
        IEnvironmentResources resources,
        IFileShareService fileShare,
        ILogger<AptSpectrPdfConverter> logger)
    {
        _resources = resources;
        _fileShare = fileShare;
        _logger = logger;
    }

    public async Task<string> ConvertToPdfAsync(
        string sourceFilePath,
        string outputPdfPath,
        bool applyTransformations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(sourceFilePath);
        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFilePath, outputPdfPath, overwrite: true);
            return outputPdfPath;
        }

        string? logoPath = null;
        if (applyTransformations)
        {
            logoPath = await TryDownloadLogoAsync(cancellationToken);
        }

        if (ImageExtensions.Contains(extension))
        {
            ConvertImageToPdf(sourceFilePath, outputPdfPath, logoPath);
            return outputPdfPath;
        }

        ConvertTextToPdf(sourceFilePath, outputPdfPath, applyTransformations, logoPath);
        return outputPdfPath;
    }

    private async Task<string?> TryDownloadLogoAsync(CancellationToken cancellationToken)
    {
        var logoSharePath = _resources.LogoSharePath?.Trim();
        if (string.IsNullOrWhiteSpace(logoSharePath))
        {
            return null;
        }

        if (!await _fileShare.ExistsAsync(logoSharePath, cancellationToken))
        {
            _logger.LogWarning("Logo not found at {LogoSharePath}; continuing without logo.", logoSharePath);
            return null;
        }

        var extension = Path.GetExtension(logoSharePath);
        var tempLogo = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        await _fileShare.DownloadToFileAsync(logoSharePath, tempLogo, cancellationToken);
        return tempLogo;
    }

    private void ConvertImageToPdf(string imagePath, string outputPdfPath, string? logoPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var image = XImage.FromFile(imagePath);

        var contentTop = Margin;
        if (logoPath is not null)
        {
            contentTop = DrawLogo(gfx, page, logoPath) + 10;
        }

        var availableHeight = page.Height.Point - contentTop - Margin;
        var availableWidth = page.Width.Point - (Margin * 2);
        gfx.DrawImage(image, Margin, contentTop, availableWidth, availableHeight);

        document.Save(outputPdfPath);
        TryDeleteTempFile(logoPath);
    }

    private void ConvertTextToPdf(
        string textPath,
        string outputPdfPath,
        bool applyTransformations,
        string? logoPath)
    {
        var content = File.ReadAllText(textPath);
        using var document = new PdfDocument();

        PdfPage? page = null;
        XGraphics? gfx = null;
        var y = Margin;
        var maxWidth = 0d;
        var logoDrawn = false;

        void NewPage()
        {
            gfx?.Dispose();
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            maxWidth = page.Width.Point - (Margin * 2);
            y = Margin;

            if (!logoDrawn && logoPath is not null)
            {
                y = DrawLogo(gfx, page, logoPath) + 10;
                logoDrawn = true;
            }
        }

        NewPage();

        foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (y > page!.Height.Point - Margin)
            {
                NewPage();
            }

            if (applyTransformations)
            {
                DrawFormattedLine(gfx!, line, Margin, ref y, maxWidth);
            }
            else
            {
                var font = new XFont("Courier New", 10, XFontStyleEx.Regular);
                gfx!.DrawString(line, font, XBrushes.Black, new XRect(Margin, y, maxWidth, LineHeight), XStringFormats.TopLeft);
                y += LineHeight;
            }
        }

        gfx!.Dispose();
        document.Save(outputPdfPath);
        TryDeleteTempFile(logoPath);
    }

    private static double DrawLogo(XGraphics gfx, PdfPage page, string logoPath)
    {
        using var logo = XImage.FromFile(logoPath);
        var maxWidth = page.Width.Point - (Margin * 2);
        var aspectRatio = (double)logo.PixelWidth / logo.PixelHeight;
        var height = LogoMaxHeight;
        var width = height * aspectRatio;
        if (width > maxWidth)
        {
            width = maxWidth;
            height = width / aspectRatio;
        }

        gfx.DrawImage(logo, Margin, Margin, width, height);
        return Margin + height;
    }

    private static void DrawFormattedLine(XGraphics gfx, string line, double x, ref double y, double maxWidth)
    {
        var segments = ParseFormattedSegments(line);
        var currentX = x;
        var lineTop = y;

        foreach (var segment in segments)
        {
            var style = segment.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
            var font = new XFont("Courier New", 10, style);
            gfx.DrawString(segment.Text, font, XBrushes.Black, currentX, lineTop);

            var size = gfx.MeasureString(segment.Text, font);
            if (segment.Underline)
            {
                var underlineY = lineTop + size.Height - 2;
                gfx.DrawLine(XPens.Black, currentX, underlineY, currentX + size.Width, underlineY);
            }

            currentX += size.Width;
        }

        y += LineHeight;
    }

    internal static IReadOnlyList<FormattedSegment> ParseFormattedSegments(string line)
    {
        var segments = new List<FormattedSegment>();
        var pattern = new Regex(@"\*\*(.+?)\*\*|__(.+?)__|([^*_]+|\*|_)", RegexOptions.Singleline);
        var matches = pattern.Matches(line);

        if (matches.Count == 0)
        {
            segments.Add(new FormattedSegment(line, false, false));
            return segments;
        }

        foreach (Match match in matches)
        {
            if (match.Groups[1].Success)
            {
                segments.Add(new FormattedSegment(match.Groups[1].Value, true, false));
            }
            else if (match.Groups[2].Success)
            {
                segments.Add(new FormattedSegment(match.Groups[2].Value, false, true));
            }
            else if (match.Groups[3].Success && match.Groups[3].Value.Length > 0)
            {
                segments.Add(new FormattedSegment(match.Groups[3].Value, false, false));
            }
        }

        return segments.Count == 0
            ? new[] { new FormattedSegment(line, false, false) }
            : segments;
    }

    private static void TryDeleteTempFile(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for temp logo download.
        }
    }

    internal readonly record struct FormattedSegment(string Text, bool Bold, bool Underline);
}
