using System.Globalization;
using System.Text;
using TabLoadFunction.Models;

namespace TabLoadFunction.Services;

public interface ISqlBulkFormatFileParser
{
    SqlBulkFormatFile Parse(string fmtContent);
}

public sealed class SqlBulkFormatFileParser : ISqlBulkFormatFileParser
{
    public SqlBulkFormatFile Parse(string fmtContent)
    {
        if (string.IsNullOrWhiteSpace(fmtContent))
        {
            throw new InvalidOperationException("Format file content is empty.");
        }

        var lines = fmtContent
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length < 2)
        {
            throw new InvalidOperationException("Format file must contain a version row and column count.");
        }

        var version = lines[0].Trim();
        if (!int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnCount))
        {
            throw new InvalidOperationException("Format file column count is invalid.");
        }

        if (lines.Length < 2 + columnCount)
        {
            throw new InvalidOperationException("Format file is missing one or more column definitions.");
        }

        var columns = new List<SqlBulkFormatColumn>(columnCount);
        for (var index = 0; index < columnCount; index++)
        {
            columns.Add(ParseColumnLine(lines[2 + index]));
        }

        return new SqlBulkFormatFile
        {
            Version = version,
            Columns = columns
        };
    }

    private static SqlBulkFormatColumn ParseColumnLine(string line)
    {
        var tokens = SplitFormatLine(line);
        if (tokens.Count < 7)
        {
            throw new InvalidOperationException($"Invalid format file column line: {line}");
        }

        return new SqlBulkFormatColumn
        {
            HostFileOrder = int.Parse(tokens[0], CultureInfo.InvariantCulture),
            SqlDataType = tokens[1],
            PrefixLength = int.Parse(tokens[2], CultureInfo.InvariantCulture),
            HostDataWidth = int.Parse(tokens[3], CultureInfo.InvariantCulture),
            FieldTerminator = UnescapeTerminator(tokens[4]),
            ColumnNumber = int.Parse(tokens[5], CultureInfo.InvariantCulture),
            ServerColumnName = tokens[6].Trim('"')
        };
    }

    private static List<string> SplitFormatLine(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                current.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string UnescapeTerminator(string token)
    {
        var trimmed = token.Trim().Trim('"');
        return trimmed
            .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
    }
}
