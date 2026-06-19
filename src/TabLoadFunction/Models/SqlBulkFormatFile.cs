namespace TabLoadFunction.Models;

public sealed class SqlBulkFormatFile
{
    public string Version { get; init; } = "";

    public IReadOnlyList<SqlBulkFormatColumn> Columns { get; init; } = Array.Empty<SqlBulkFormatColumn>();
}

public sealed class SqlBulkFormatColumn
{
    public int HostFileOrder { get; init; }

    public string SqlDataType { get; init; } = "";

    public int PrefixLength { get; init; }

    public int HostDataWidth { get; init; }

    public string FieldTerminator { get; init; } = "";

    public int ColumnNumber { get; init; }

    public string ServerColumnName { get; init; } = "";
}
