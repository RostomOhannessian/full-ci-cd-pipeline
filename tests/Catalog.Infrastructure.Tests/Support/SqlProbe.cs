using Microsoft.Data.SqlClient;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>Small queries against SQL Server for the tests that look at the database itself: the schema, the indexes, the sessions, and the rows that the code under test wrote.</summary>
internal static class SqlProbe
{
    /// <summary>Runs a query and returns the first column of the first row, or the default of the type when there is no row.</summary>
    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a query and returns every row as the text of its columns.</summary>
    public static async Task<IReadOnlyList<string[]>> RowsAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();

        List<string[]> rows = [];
        while (await reader.ReadAsync())
        {
            var row = new string[reader.FieldCount];
            for (var column = 0; column < row.Length; column++)
            {
                row[column] = Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
