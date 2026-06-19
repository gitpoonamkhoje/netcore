namespace TabLoadFunction.Models;

public sealed class QualifiedTableName
{
    public string DatabaseName { get; init; } = "";

    public string SchemaName { get; init; } = "dbo";

    public string TableName { get; init; } = "";

    public string OsFileName { get; init; } = "";

    public string QualifiedName =>
        string.IsNullOrWhiteSpace(DatabaseName)
            ? $"[{SchemaName}].[{TableName}]"
            : $"[{DatabaseName}].[{SchemaName}].[{TableName}]";
}
