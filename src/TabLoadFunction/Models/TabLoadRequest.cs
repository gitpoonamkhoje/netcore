namespace TabLoadFunction.Models;

public sealed class TabLoadRequest
{
    /// <summary>
    /// Fully qualified table name, e.g. MyDb.dbo.MyTable.
    /// </summary>
    public string TableName { get; set; } = "";

    public string BackupTable { get; set; } = "Y";

    public string ChildProcess { get; set; } = "N";

    public string LoadFailureOverride { get; set; } = "N";

    public string FlatFileDateCheck { get; set; } = "Y";

    public string Delimiter { get; set; } = "~";

    public string Zipped { get; set; } = "N";

    public string AlternateName { get; set; } = "N";

    public string FtpPath { get; set; } = "";

    public string AppId { get; set; } = "N/A";

    public int MaxErrors { get; set; } = 2;

    public int PollItemId { get; set; }

    public bool ExecuteDdlScript { get; set; } = true;

    public bool ExecuteIndexScript { get; set; } = true;
}
