namespace TabLoadFunction.Models;

public sealed class TabLoadResult
{
    public bool Succeeded { get; set; }

    public string TableName { get; set; } = "";

    public string DataFilePath { get; set; } = "";

    public string? FormatFilePath { get; set; }

    public bool UsedFormatFile { get; set; }

    public int RowsLoaded { get; set; }

    public string LoadMode { get; set; } = "";

    public IReadOnlyList<string> LogMessages { get; set; } = Array.Empty<string>();

    public string? ErrorMessage { get; set; }
}
