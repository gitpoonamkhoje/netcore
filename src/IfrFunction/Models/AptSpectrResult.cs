namespace IfrFunction.Models;

public sealed class AptSpectrFileResult
{
    public string Source { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string SourceSharePath { get; set; } = "";
    public string? PdfSharePath { get; set; }
    public string? ArchiveSharePath { get; set; }
    public bool Succeeded { get; set; }
    public bool Skipped { get; set; }
    public string? ErrorMessage { get; set; }
    public long? MetadataId { get; set; }
}

public sealed class AptSpectrResult
{
    public bool Succeeded { get; set; }
    public string Source { get; set; } = "";
    public IReadOnlyList<AptSpectrFileResult> Files { get; set; } = Array.Empty<AptSpectrFileResult>();
    public IReadOnlyList<string> LogMessages { get; set; } = Array.Empty<string>();
    public string? ErrorMessage { get; set; }
}
