using System.Data;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using TabLoadFunction.Configuration;
using TabLoadFunction.Models;

namespace TabLoadFunction.Services;

public interface ITabLoadService
{
    Task<TabLoadResult> LoadAsync(TabLoadRequest request, CancellationToken cancellationToken);
}

public sealed class TabLoadService : ITabLoadService
{
    private readonly IEnvironmentResources _resources;
    private readonly IFileShareReader _fileShareReader;
    private readonly ISqlBulkFormatFileParser _formatFileParser;
    private readonly ILogger<TabLoadService> _logger;

    public TabLoadService(
        IEnvironmentResources resources,
        IFileShareReader fileShareReader,
        ISqlBulkFormatFileParser formatFileParser,
        ILogger<TabLoadService> logger)
    {
        _resources = resources;
        _fileShareReader = fileShareReader;
        _formatFileParser = formatFileParser;
        _logger = logger;
    }

    public async Task<TabLoadResult> LoadAsync(TabLoadRequest request, CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var table = ParseTableName(request.TableName);
        var ftpRoot = CombineSharePath(_resources.FtpSharePath, request.FtpPath);
        var ddlRoot = _resources.DdlSharePath.Trim('/');
        var zipped = IsYes(request.Zipped);
        var backupEnabled = IsYes(request.BackupTable);
        var dateCheckEnabled = IsYes(request.FlatFileDateCheck);
        var loadFailureOverride = IsYes(request.LoadFailureOverride);
        var childProcess = IsYes(request.ChildProcess);
        var extension = zipped ? ".zip" : ".txt";
        var dataSharePath = $"{ftpRoot}/{table.OsFileName}{extension}";
        var formatSharePath = $"{ddlRoot}/{table.OsFileName}.fmt";
        var ddlSharePath = $"{ddlRoot}/{table.OsFileName}.sql";
        var indexSharePath = $"{ddlRoot}/{table.OsFileName}.idx";
        string? backupTableName = null;
        var tempDataPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");

        try
        {
            Log(logs, $"{table.OsFileName} : TABLOAD start for {table.QualifiedName}");

            if (!await TableExistsAsync(table, cancellationToken))
            {
                throw new InvalidOperationException($"Table {table.QualifiedName} does not exist.");
            }

            if (!await _fileShareReader.ExistsAsync(dataSharePath, cancellationToken))
            {
                throw new FileNotFoundException($"Data file '{dataSharePath}' was not found on the file share.");
            }

            if (dateCheckEnabled)
            {
                var properties = await _fileShareReader.GetPropertiesAsync(dataSharePath, cancellationToken);
                if (properties.LastModified.UtcDateTime.Date != DateTime.UtcNow.Date)
                {
                    throw new InvalidOperationException(
                        $"Data file '{dataSharePath}' does not have today's date. Load skipped.");
                }
            }

            if (request.ExecuteDdlScript && await _fileShareReader.ExistsAsync(ddlSharePath, cancellationToken))
            {
                var ddlScript = await _fileShareReader.ReadAllTextAsync(ddlSharePath, cancellationToken);
                await ExecuteSqlBatchesAsync(ddlScript, cancellationToken);
                Log(logs, $"{table.OsFileName} : Executed DDL script {ddlSharePath}");
            }

            if (backupEnabled)
            {
                backupTableName = await BackupTableAsync(table, cancellationToken);
                Log(logs, $"{table.OsFileName} : Backup created in {backupTableName}");
            }

            await PrepareDataFileAsync(
                dataSharePath,
                tempDataPath,
                zipped,
                request.AlternateName,
                table.OsFileName,
                ftpRoot,
                cancellationToken);

            var hasFormatFile = await _fileShareReader.ExistsAsync(formatSharePath, cancellationToken);
            int rowsLoaded;
            string loadMode;

            if (hasFormatFile)
            {
                var formatContent = await _fileShareReader.ReadAllTextAsync(formatSharePath, cancellationToken);
                var formatFile = _formatFileParser.Parse(formatContent);
                rowsLoaded = await LoadUsingFormatFileAsync(table, tempDataPath, formatFile, cancellationToken);
                loadMode = "FormatFile";
                Log(logs, $"{table.OsFileName} : Loaded {rowsLoaded} row(s) using format file.");
            }
            else
            {
                rowsLoaded = await LoadUsingDelimiterAsync(table, tempDataPath, request.Delimiter, cancellationToken);
                loadMode = "Delimiter";
                Log(logs, $"{table.OsFileName} : Loaded {rowsLoaded} row(s) using delimiter '{request.Delimiter}'.");
            }

            if (request.ExecuteIndexScript && await _fileShareReader.ExistsAsync(indexSharePath, cancellationToken))
            {
                var indexScript = await _fileShareReader.ReadAllTextAsync(indexSharePath, cancellationToken);
                await ExecuteSqlBatchesAsync(indexScript, cancellationToken);
                Log(logs, $"{table.OsFileName} : Executed index script {indexSharePath}");
            }

            if (childProcess)
            {
                await UpdatePollProcessStatusAsync(table.OsFileName, request.AppId, succeeded: true, cancellationToken);
                Log(logs, $"{table.OsFileName} : poll_process status updated to C");
            }

            if (backupTableName is not null)
            {
                await DropBackupTableAsync(backupTableName, cancellationToken);
            }

            Log(logs, $"{table.OsFileName} : TABLOAD completed successfully");

            return new TabLoadResult
            {
                Succeeded = true,
                TableName = table.QualifiedName,
                DataFilePath = dataSharePath,
                FormatFilePath = hasFormatFile ? formatSharePath : null,
                UsedFormatFile = hasFormatFile,
                RowsLoaded = rowsLoaded,
                LoadMode = loadMode,
                LogMessages = logs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TABLOAD failed for {TableName}", table.QualifiedName);
            Log(logs, $"{table.OsFileName} : TABLOAD failed - {ex.Message}");

            if (!loadFailureOverride && backupEnabled && backupTableName is not null)
            {
                try
                {
                    await RestoreTableAsync(table, backupTableName, cancellationToken);
                    Log(logs, $"{table.OsFileName} : Backup restored from {backupTableName}");
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "TABLOAD restore failed for {TableName}", table.QualifiedName);
                    Log(logs, $"{table.OsFileName} : Backup restore failed - {restoreEx.Message}");
                }
            }

            if (childProcess)
            {
                await UpdatePollProcessStatusAsync(table.OsFileName, request.AppId, succeeded: false, cancellationToken);
                Log(logs, $"{table.OsFileName} : poll_process status updated to F");
            }

            return new TabLoadResult
            {
                Succeeded = false,
                TableName = table.QualifiedName,
                DataFilePath = dataSharePath,
                FormatFilePath = null,
                UsedFormatFile = false,
                RowsLoaded = 0,
                LoadMode = "",
                LogMessages = logs,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            if (File.Exists(tempDataPath))
            {
                File.Delete(tempDataPath);
            }
        }
    }

    private async Task PrepareDataFileAsync(
        string dataSharePath,
        string tempDataPath,
        bool zipped,
        string alternateName,
        string osFileName,
        string ftpRoot,
        CancellationToken cancellationToken)
    {
        if (!zipped)
        {
            await _fileShareReader.DownloadToFileAsync(dataSharePath, tempDataPath, cancellationToken);
            return;
        }

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var extractDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await _fileShareReader.DownloadToFileAsync(dataSharePath, tempZipPath, cancellationToken);
            ZipFile.ExtractToDirectory(tempZipPath, extractDirectory, overwriteFiles: true);

            var extractedFileName = !IsNo(alternateName)
                ? alternateName
                : $"{osFileName}.txt";

            var extractedPath = Path.Combine(extractDirectory, extractedFileName);
            if (!File.Exists(extractedPath))
            {
                extractedPath = Directory
                    .EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(file =>
                        string.Equals(Path.GetFileName(file), extractedFileName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException(
                        $"Extracted file '{extractedFileName}' was not found in zip archive.");
            }

            File.Copy(extractedPath, tempDataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            if (Directory.Exists(extractDirectory))
            {
                Directory.Delete(extractDirectory, recursive: true);
            }
        }
    }

    private async Task<int> LoadUsingFormatFileAsync(
        QualifiedTableName table,
        string dataFilePath,
        SqlBulkFormatFile formatFile,
        CancellationToken cancellationToken)
    {
        var targetColumns = await GetTableColumnsAsync(table, cancellationToken);
        var orderedColumns = formatFile.Columns
            .OrderBy(column => column.HostFileOrder)
            .ToArray();

        var dataTable = new DataTable();
        foreach (var column in orderedColumns)
        {
            var targetColumn = targetColumns.FirstOrDefault(
                candidate => string.Equals(candidate, column.ServerColumnName, StringComparison.OrdinalIgnoreCase));

            if (targetColumn is null)
            {
                throw new InvalidOperationException(
                    $"Format file column '{column.ServerColumnName}' was not found on table {table.QualifiedName}.");
            }

            dataTable.Columns.Add(targetColumn, typeof(string));
        }

        var fileContent = await File.ReadAllTextAsync(dataFilePath, cancellationToken);
        foreach (var rowValues in ReadRowsFromFormatFile(fileContent, orderedColumns))
        {
            dataTable.Rows.Add(rowValues);
        }

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = table.QualifiedName,
            BatchSize = 5000,
            BulkCopyTimeout = 0
        };

        foreach (DataColumn column in dataTable.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
        return dataTable.Rows.Count;
    }

    private async Task<int> LoadUsingDelimiterAsync(
        QualifiedTableName table,
        string dataFilePath,
        string delimiter,
        CancellationToken cancellationToken)
    {
        var columns = await GetTableColumnsAsync(table, cancellationToken);
        var dataTable = new DataTable();
        foreach (var column in columns)
        {
            dataTable.Columns.Add(column, typeof(string));
        }

        await foreach (var line in ReadLinesAsync(dataFilePath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = line.Split(delimiter);
            if (values.Length != columns.Count)
            {
                throw new InvalidOperationException(
                    $"Delimiter row has {values.Length} value(s) but table {table.QualifiedName} has {columns.Count} column(s).");
            }

            dataTable.Rows.Add(values);
        }

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = table.QualifiedName,
            BatchSize = 5000,
            BulkCopyTimeout = 0
        };

        foreach (DataColumn column in dataTable.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
        return dataTable.Rows.Count;
    }

    private static IEnumerable<object[]> ReadRowsFromFormatFile(
        string fileContent,
        IReadOnlyList<SqlBulkFormatColumn> columns)
    {
        var position = 0;
        while (position < fileContent.Length)
        {
            var values = new object[columns.Count];
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                string value;

                if (column.HostDataWidth > 0)
                {
                    if (position + column.HostDataWidth > fileContent.Length)
                    {
                        yield break;
                    }

                    value = fileContent.Substring(position, column.HostDataWidth).Trim();
                    position += column.HostDataWidth;
                }
                else
                {
                    var terminator = string.IsNullOrEmpty(column.FieldTerminator)
                        ? "\r\n"
                        : column.FieldTerminator;
                    var terminatorIndex = fileContent.IndexOf(terminator, position, StringComparison.Ordinal);
                    if (terminatorIndex < 0)
                    {
                        value = fileContent[position..].Trim();
                        position = fileContent.Length;
                    }
                    else
                    {
                        value = fileContent[position..terminatorIndex].Trim();
                        position = terminatorIndex + terminator.Length;
                    }
                }

                values[index] = value;
            }

            if (values.All(value => string.IsNullOrWhiteSpace(value?.ToString())))
            {
                continue;
            }

            yield return values;

            if (position < fileContent.Length && fileContent[position] == '\r')
            {
                position++;
            }

            if (position < fileContent.Length && fileContent[position] == '\n')
            {
                position++;
            }
        }
    }

    private async Task<bool> TableExistsAsync(QualifiedTableName table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @TableName
              AND s.name = @SchemaName
            """;

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", table.TableName);
        command.Parameters.AddWithValue("@SchemaName", table.SchemaName);

        var count = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return count > 0;
    }

    private async Task<IReadOnlyList<string>> GetTableColumnsAsync(
        QualifiedTableName table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.name
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.name = @TableName
              AND s.name = @SchemaName
            ORDER BY c.column_id
            """;

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TableName", table.TableName);
        command.Parameters.AddWithValue("@SchemaName", table.SchemaName);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"No columns were found for table {table.QualifiedName}.");
        }

        return columns;
    }

    private async Task<string> BackupTableAsync(QualifiedTableName table, CancellationToken cancellationToken)
    {
        var backupTableName = $"TabLoadBackup_{Guid.NewGuid():N}";
        var sql = $"SELECT * INTO [{backupTableName}] FROM {table.QualifiedName};";

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return backupTableName;
    }

    private async Task RestoreTableAsync(
        QualifiedTableName table,
        string backupTableName,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            TRUNCATE TABLE {table.QualifiedName};
            INSERT INTO {table.QualifiedName}
            SELECT * FROM [{backupTableName}];
            DROP TABLE [{backupTableName}];
            """;

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DropBackupTableAsync(string backupTableName, CancellationToken cancellationToken)
    {
        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"DROP TABLE IF EXISTS [{backupTableName}];", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteSqlBatchesAsync(string script, CancellationToken cancellationToken)
    {
        var batches = script.Split(
            new[] { "\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = new SqlCommand(batch, connection)
            {
                CommandTimeout = 0
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task UpdatePollProcessStatusAsync(
        string osFileName,
        string appId,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE poll_process
            SET Status = @Status,
                jobstat_tx = @Status
            WHERE filename_tx LIKE @FileNamePattern
              AND (@AppId = 'N/A' OR app_id = @AppId OR AppId = @AppId)
            """;

        await using var connection = _resources.CreateSqlConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Status", succeeded ? "C" : "F");
        command.Parameters.AddWithValue("@FileNamePattern", $"{osFileName}%");
        command.Parameters.AddWithValue("@AppId", appId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static QualifiedTableName ParseTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name is required.", nameof(tableName));
        }

        var normalized = tableName.Trim();
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            1 => new QualifiedTableName
            {
                SchemaName = "dbo",
                TableName = StripBrackets(parts[0]),
                OsFileName = StripBrackets(parts[0]).ToUpperInvariant()
            },
            2 => new QualifiedTableName
            {
                SchemaName = StripBrackets(parts[0]),
                TableName = StripBrackets(parts[1]),
                OsFileName = StripBrackets(parts[1]).ToUpperInvariant()
            },
            _ => new QualifiedTableName
            {
                DatabaseName = StripBrackets(parts[0]),
                SchemaName = StripBrackets(parts[1]),
                TableName = StripBrackets(parts[2]),
                OsFileName = StripBrackets(parts[2]).ToUpperInvariant()
            }
        };
    }

    private static string StripBrackets(string value) =>
        value.Trim().Trim('[', ']');

    private static string CombineSharePath(string basePath, string subPath)
    {
        var normalizedBase = basePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(subPath))
        {
            return normalizedBase;
        }

        var normalizedSub = subPath.Replace('\\', '/').Trim('/');
        return $"{normalizedBase}/{normalizedSub}";
    }

    private static bool IsYes(string? value) =>
        string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, "N", StringComparison.OrdinalIgnoreCase);

    private static void Log(List<string> logs, string message)
    {
        logs.Add($"{DateTime.UtcNow:O} {message}");
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }
}
