using Microsoft.Data.SqlClient;

namespace PollProcessorFunction.Services;

internal static class SqlDataReaderExtensions
{
    public static bool TryGetOrdinal(this SqlDataReader reader, string name, out int ordinal)
    {
        try
        {
            ordinal = reader.GetOrdinal(name);
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            ordinal = -1;
            return false;
        }
    }

    public static object? GetValueOrNull(this SqlDataReader reader, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (!reader.TryGetOrdinal(name, out var ordinal) || reader.IsDBNull(ordinal))
            {
                continue;
            }

            return reader.GetValue(ordinal);
        }

        return null;
    }

    public static string? GetStringOrNull(this SqlDataReader reader, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (!reader.TryGetOrdinal(name, out var ordinal) || reader.IsDBNull(ordinal))
            {
                continue;
            }

            return reader.GetValue(ordinal)?.ToString();
        }

        return null;
    }

    public static int? GetInt32OrNull(this SqlDataReader reader, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (!reader.TryGetOrdinal(name, out var ordinal) || reader.IsDBNull(ordinal))
            {
                continue;
            }

            return Convert.ToInt32(reader.GetValue(ordinal));
        }

        return null;
    }

    public static DateTime? GetDateTimeOrNull(this SqlDataReader reader, params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (!reader.TryGetOrdinal(name, out var ordinal) || reader.IsDBNull(ordinal))
            {
                continue;
            }

            return reader.GetDateTime(ordinal);
        }

        return null;
    }
}
