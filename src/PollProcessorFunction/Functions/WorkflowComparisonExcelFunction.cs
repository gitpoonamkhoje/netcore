using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PollProcessorFunction.Functions;

public sealed class WorkflowComparisonExcelFunction
{
    private static readonly Regex SqlIdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly IConfiguration _configuration;

    public WorkflowComparisonExcelFunction(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [Function("DownloadWorkflowComparisonExcel")]
    public async Task<HttpResponseData> DownloadExcelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "workflow/compare/excel")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(req, cancellationToken);
        var oldConnectionString = GetRequiredSetting("SqlAgentValidationConnectionString");
        var newConnectionString = GetRequiredSetting("AdfValidationConnectionString");

        var tableResults = new List<TableComparisonResult>();
        foreach (var table in request.Tables)
        {
            tableResults.Add(await CompareTableAsync(
                oldConnectionString,
                newConnectionString,
                table,
                cancellationToken));
        }

        using var workbook = BuildWorkbook(request, tableResults);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition", "attachment; filename=\"workflow-sqlagent-adf-comparison.xlsx\"");

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, cancellationToken);

        return response;
    }

    private static async Task<WorkflowComparisonRequest> ReadRequestAsync(
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadAsStringAsync();
        var request = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonConvert.DeserializeObject<WorkflowComparisonRequest>(body);

        if (request is null || request.Tables.Count == 0)
        {
            throw new InvalidOperationException("Request must include at least one table to compare.");
        }

        foreach (var table in request.Tables)
        {
            table.KeyColumns ??= new List<string>();
            table.CompareColumns ??= new List<string>();
            table.Parameters ??= new JObject();

            if (string.IsNullOrWhiteSpace(table.TableName))
            {
                throw new InvalidOperationException("Each table comparison must include tableName.");
            }

            if (table.KeyColumns.Count == 0)
            {
                throw new InvalidOperationException($"Table {table.TableName} must include at least one key column.");
            }
        }

        return request;
    }

    private async Task<TableComparisonResult> CompareTableAsync(
        string oldConnectionString,
        string newConnectionString,
        TableComparisonRequest table,
        CancellationToken cancellationToken)
    {
        ValidateTableRequest(table);

        var compareColumns = table.CompareColumns.Count > 0
            ? table.CompareColumns
            : await GetCommonCompareColumnsAsync(oldConnectionString, newConnectionString, table, cancellationToken);

        var oldRows = await LoadRowsAsync(oldConnectionString, table, compareColumns, cancellationToken);
        var newRows = await LoadRowsAsync(newConnectionString, table, compareColumns, cancellationToken);

        var oldKeys = oldRows.Keys.ToHashSet(StringComparer.Ordinal);
        var newKeys = newRows.Keys.ToHashSet(StringComparer.Ordinal);
        var commonKeys = oldKeys.Intersect(newKeys, StringComparer.Ordinal).ToArray();

        var result = new TableComparisonResult
        {
            TableName = table.TableName,
            WhereClause = table.WhereClause,
            KeyColumns = table.KeyColumns.ToArray(),
            CompareColumns = compareColumns.ToArray(),
            OldRowCount = oldRows.Count,
            NewRowCount = newRows.Count,
            MissingInAdf = oldKeys.Except(newKeys, StringComparer.Ordinal)
                .Select(key => oldRows[key])
                .ToArray(),
            ExtraInAdf = newKeys.Except(oldKeys, StringComparer.Ordinal)
                .Select(key => newRows[key])
                .ToArray()
        };

        var mismatches = new List<RowMismatch>();
        foreach (var key in commonKeys)
        {
            var oldRow = oldRows[key];
            var newRow = newRows[key];
            if (!string.Equals(oldRow.Hash, newRow.Hash, StringComparison.Ordinal))
            {
                mismatches.Add(new RowMismatch
                {
                    Key = key,
                    OldHash = oldRow.Hash,
                    NewHash = newRow.Hash,
                    ColumnDifferences = BuildColumnDifferences(oldRow, newRow, compareColumns)
                });
            }
        }

        result.MismatchedRows = mismatches;
        result.MatchedRowCount = commonKeys.Length - mismatches.Count;
        return result;
    }

    private static IReadOnlyList<ColumnDifference> BuildColumnDifferences(
        RowSnapshot oldRow,
        RowSnapshot newRow,
        IReadOnlyList<string> compareColumns)
    {
        var differences = new List<ColumnDifference>();
        foreach (var column in compareColumns)
        {
            oldRow.Values.TryGetValue(column, out var oldValue);
            newRow.Values.TryGetValue(column, out var newValue);

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                differences.Add(new ColumnDifference
                {
                    ColumnName = column,
                    OldValue = oldValue ?? "",
                    NewValue = newValue ?? ""
                });
            }
        }

        return differences;
    }

    private async Task<IReadOnlyDictionary<string, RowSnapshot>> LoadRowsAsync(
        string connectionString,
        TableComparisonRequest table,
        IReadOnlyList<string> compareColumns,
        CancellationToken cancellationToken)
    {
        var selectedColumns = table.KeyColumns
            .Concat(compareColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sql = BuildSelectSql(table, selectedColumns);
        var rows = new Dictionary<string, RowSnapshot>(StringComparer.Ordinal);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        AddParameters(cmd, table.Parameters);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = BuildKey(reader, table.KeyColumns);
            if (rows.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Duplicate key '{key}' found in {table.TableName}. Use keyColumns that uniquely identify rows.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in compareColumns)
            {
                values[column] = GetValue(reader, column);
            }

            rows[key] = new RowSnapshot
            {
                Key = key,
                Values = values,
                Hash = ComputeHash(values, compareColumns)
            };
        }

        return rows;
    }

    private async Task<IReadOnlyList<string>> GetCommonCompareColumnsAsync(
        string oldConnectionString,
        string newConnectionString,
        TableComparisonRequest table,
        CancellationToken cancellationToken)
    {
        var oldColumns = await GetTableColumnsAsync(oldConnectionString, table.TableName, cancellationToken);
        var newColumns = await GetTableColumnsAsync(newConnectionString, table.TableName, cancellationToken);

        return oldColumns
            .Intersect(newColumns, StringComparer.OrdinalIgnoreCase)
            .Except(table.KeyColumns, StringComparer.OrdinalIgnoreCase)
            .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> GetTableColumnsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        var (schema, name) = SplitTableName(tableName);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(@"
SELECT c.name
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = @schema AND t.name = @table
  AND c.is_computed = 0
ORDER BY c.column_id;", conn);

        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", name);

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Table {tableName} was not found or has no comparable columns.");
        }

        return columns;
    }

    private static string BuildSelectSql(TableComparisonRequest table, IReadOnlyList<string> selectedColumns)
    {
        var columns = string.Join(", ", selectedColumns.Select(QuoteIdentifier));
        var sql = $"SELECT {columns} FROM {QuoteTableName(table.TableName)}";

        if (!string.IsNullOrWhiteSpace(table.WhereClause))
        {
            sql += $" WHERE {table.WhereClause}";
        }

        return sql;
    }

    private static void AddParameters(SqlCommand cmd, JObject parameters)
    {
        foreach (var parameter in parameters.Properties())
        {
            var parameterName = parameter.Name.StartsWith('@')
                ? parameter.Name
                : $"@{parameter.Name}";

            cmd.Parameters.AddWithValue(parameterName, ToParameterValue(parameter.Value));
        }
    }

    private static object ToParameterValue(JToken value)
    {
        return value.Type switch
        {
            JTokenType.Null => DBNull.Value,
            JTokenType.Integer => value.Value<long>(),
            JTokenType.Float => value.Value<double>(),
            JTokenType.Boolean => value.Value<bool>(),
            JTokenType.Date => value.Value<DateTime>(),
            _ => value.Value<string>() ?? ""
        };
    }

    private static string BuildKey(SqlDataReader reader, IReadOnlyList<string> keyColumns)
    {
        return string.Join("||", keyColumns.Select(column => GetValue(reader, column)));
    }

    private static string GetValue(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is null or DBNull
            ? "<NULL>"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    private static string ComputeHash(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> compareColumns)
    {
        var builder = new StringBuilder();
        foreach (var column in compareColumns)
        {
            values.TryGetValue(column, out var value);
            builder.Append(column).Append('=').Append(value?.Length ?? 0).Append(':').Append(value).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static XLWorkbook BuildWorkbook(
        WorkflowComparisonRequest request,
        IReadOnlyList<TableComparisonResult> tableResults)
    {
        var workbook = new XLWorkbook();
        AddSummarySheet(workbook, request, tableResults);
        AddTableResultsSheet(workbook, tableResults);
        AddMismatchSheet(workbook, tableResults);
        AddMissingSheet(workbook, "Missing In ADF", tableResults.SelectMany(result => result.MissingInAdf.Select(row => (result.TableName, row))));
        AddMissingSheet(workbook, "Extra In ADF", tableResults.SelectMany(result => result.ExtraInAdf.Select(row => (result.TableName, row))));
        AddRequestSheet(workbook, request);
        return workbook;
    }

    private static void AddSummarySheet(
        XLWorkbook workbook,
        WorkflowComparisonRequest request,
        IReadOnlyList<TableComparisonResult> tableResults)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        WriteHeader(sheet, "Metric", "Value");

        sheet.Cell(2, 1).Value = "Workflow Name";
        sheet.Cell(2, 2).Value = request.WorkflowName;
        sheet.Cell(3, 1).Value = "Generated Utc";
        sheet.Cell(3, 2).Value = DateTime.UtcNow.ToString("O");
        sheet.Cell(4, 1).Value = "Tables Compared";
        sheet.Cell(4, 2).Value = tableResults.Count;
        sheet.Cell(5, 1).Value = "Total Old Rows";
        sheet.Cell(5, 2).Value = tableResults.Sum(result => result.OldRowCount);
        sheet.Cell(6, 1).Value = "Total ADF Rows";
        sheet.Cell(6, 2).Value = tableResults.Sum(result => result.NewRowCount);
        sheet.Cell(7, 1).Value = "Mismatched Rows";
        sheet.Cell(7, 2).Value = tableResults.Sum(result => result.MismatchedRows.Count);
        sheet.Cell(8, 1).Value = "Missing In ADF";
        sheet.Cell(8, 2).Value = tableResults.Sum(result => result.MissingInAdf.Count);
        sheet.Cell(9, 1).Value = "Extra In ADF";
        sheet.Cell(9, 2).Value = tableResults.Sum(result => result.ExtraInAdf.Count);
        FormatSheet(sheet);
    }

    private static void AddTableResultsSheet(XLWorkbook workbook, IReadOnlyList<TableComparisonResult> tableResults)
    {
        var sheet = workbook.Worksheets.Add("Table Results");
        WriteHeader(
            sheet,
            "Table Name",
            "Where Clause",
            "Key Columns",
            "Compare Columns",
            "Old SQL Agent Rows",
            "New ADF Rows",
            "Matched Rows",
            "Mismatched Rows",
            "Missing In ADF",
            "Extra In ADF",
            "Match Status");

        var row = 2;
        foreach (var result in tableResults)
        {
            sheet.Cell(row, 1).Value = result.TableName;
            sheet.Cell(row, 2).Value = result.WhereClause;
            sheet.Cell(row, 3).Value = string.Join(", ", result.KeyColumns);
            sheet.Cell(row, 4).Value = string.Join(", ", result.CompareColumns);
            sheet.Cell(row, 5).Value = result.OldRowCount;
            sheet.Cell(row, 6).Value = result.NewRowCount;
            sheet.Cell(row, 7).Value = result.MatchedRowCount;
            sheet.Cell(row, 8).Value = result.MismatchedRows.Count;
            sheet.Cell(row, 9).Value = result.MissingInAdf.Count;
            sheet.Cell(row, 10).Value = result.ExtraInAdf.Count;
            sheet.Cell(row, 11).Value = result.IsMatch ? "Match" : "Fail";
            row++;
        }

        FormatSheet(sheet);
    }

    private static void AddMismatchSheet(XLWorkbook workbook, IReadOnlyList<TableComparisonResult> tableResults)
    {
        var sheet = workbook.Worksheets.Add("Column Differences");
        WriteHeader(sheet, "Table Name", "Key", "Column Name", "Old SQL Agent Value", "New ADF Value", "Old Hash", "New Hash");

        var row = 2;
        foreach (var result in tableResults)
        {
            foreach (var mismatch in result.MismatchedRows)
            {
                foreach (var difference in mismatch.ColumnDifferences)
                {
                    sheet.Cell(row, 1).Value = result.TableName;
                    sheet.Cell(row, 2).Value = mismatch.Key;
                    sheet.Cell(row, 3).Value = difference.ColumnName;
                    sheet.Cell(row, 4).Value = difference.OldValue;
                    sheet.Cell(row, 5).Value = difference.NewValue;
                    sheet.Cell(row, 6).Value = mismatch.OldHash;
                    sheet.Cell(row, 7).Value = mismatch.NewHash;
                    row++;
                }
            }
        }

        FormatSheet(sheet);
    }

    private static void AddMissingSheet(
        XLWorkbook workbook,
        string sheetName,
        IEnumerable<(string TableName, RowSnapshot Row)> rows)
    {
        var sheet = workbook.Worksheets.Add(sheetName);
        WriteHeader(sheet, "Table Name", "Key", "Row Hash");

        var rowNumber = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowNumber, 1).Value = row.TableName;
            sheet.Cell(rowNumber, 2).Value = row.Row.Key;
            sheet.Cell(rowNumber, 3).Value = row.Row.Hash;
            rowNumber++;
        }

        FormatSheet(sheet);
    }

    private static void AddRequestSheet(XLWorkbook workbook, WorkflowComparisonRequest request)
    {
        var sheet = workbook.Worksheets.Add("Request");
        WriteHeader(sheet, "Request Json");
        sheet.Cell(2, 1).Value = JsonConvert.SerializeObject(request, Formatting.Indented);
        FormatSheet(sheet);
    }

    private static void WriteHeader(IXLWorksheet sheet, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }
    }

    private static void FormatSheet(IXLWorksheet sheet)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange is null)
        {
            return;
        }

        usedRange.Style.Alignment.WrapText = true;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(1);
        usedRange.SetAutoFilter();
        sheet.Columns().AdjustToContents(1, 60);
    }

    private static void ValidateTableRequest(TableComparisonRequest table)
    {
        _ = QuoteTableName(table.TableName);
        foreach (var column in table.KeyColumns.Concat(table.CompareColumns))
        {
            _ = QuoteIdentifier(column);
        }
    }

    private static string QuoteTableName(string tableName)
    {
        var (schema, name) = SplitTableName(tableName);
        return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(name)}";
    }

    private static (string Schema, string Name) SplitTableName(string tableName)
    {
        var parts = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => ("dbo", parts[0]),
            2 => (parts[0], parts[1]),
            _ => throw new InvalidOperationException($"Invalid table name: {tableName}")
        };
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (!SqlIdentifierRegex.IsMatch(identifier))
        {
            throw new InvalidOperationException($"Invalid SQL identifier: {identifier}");
        }

        return $"[{identifier}]";
    }

    private string GetRequiredSetting(string name)
    {
        var value = _configuration[name]
            ?? _configuration[$"AppResources:{name}"];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is not configured.")
            : value;
    }

    private sealed class WorkflowComparisonRequest
    {
        public string WorkflowName { get; set; } = "";

        public List<TableComparisonRequest> Tables { get; set; } = new();
    }

    private sealed class TableComparisonRequest
    {
        public string TableName { get; set; } = "";

        public List<string> KeyColumns { get; set; } = new();

        public List<string> CompareColumns { get; set; } = new();

        public string WhereClause { get; set; } = "";

        public JObject Parameters { get; set; } = new();
    }

    private sealed class RowSnapshot
    {
        public string Key { get; init; } = "";

        public string Hash { get; init; } = "";

        public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    }

    private sealed class TableComparisonResult
    {
        public string TableName { get; init; } = "";

        public string WhereClause { get; init; } = "";

        public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> CompareColumns { get; init; } = Array.Empty<string>();

        public int OldRowCount { get; init; }

        public int NewRowCount { get; init; }

        public int MatchedRowCount { get; set; }

        public IReadOnlyList<RowMismatch> MismatchedRows { get; set; } = Array.Empty<RowMismatch>();

        public IReadOnlyList<RowSnapshot> MissingInAdf { get; init; } = Array.Empty<RowSnapshot>();

        public IReadOnlyList<RowSnapshot> ExtraInAdf { get; init; } = Array.Empty<RowSnapshot>();

        public bool IsMatch =>
            OldRowCount == NewRowCount
            && MismatchedRows.Count == 0
            && MissingInAdf.Count == 0
            && ExtraInAdf.Count == 0;
    }

    private sealed class RowMismatch
    {
        public string Key { get; init; } = "";

        public string OldHash { get; init; } = "";

        public string NewHash { get; init; } = "";

        public IReadOnlyList<ColumnDifference> ColumnDifferences { get; init; } = Array.Empty<ColumnDifference>();
    }

    private sealed class ColumnDifference
    {
        public string ColumnName { get; init; } = "";

        public string OldValue { get; init; } = "";

        public string NewValue { get; init; } = "";
    }
}
