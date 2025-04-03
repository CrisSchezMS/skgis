using System.ComponentModel;
using Microsoft.SemanticKernel;
using Npgsql;

public class SqlPlugin
{
    private readonly string _connectionString;

    public SqlPlugin(string connectionString)
    {
        _connectionString = connectionString;
    }

    [KernelFunction("EjecutarSQLAsync")]
    public async Task<string> EjecutarSQLAsync(string sql)
    {
        var resultados = new List<string>();
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var fila = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                fila.Add(reader[i]?.ToString());
            }
            resultados.Add(string.Join(" | ", fila));
        }

        return resultados.Count > 0
            ? string.Join("\n", resultados)
            : "No se encontraron resultados.";
    }
}