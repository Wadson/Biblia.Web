using Biblia.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Biblia.Infrastructure.BibleDatabases;

internal static class BibleBookReader
{
    internal static async Task<IReadOnlyList<BibleBook>> ReadAsync(SqliteConnection connection, CancellationToken token)
    {
        var result = new List<BibleBook>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT book_reference_id,testament_reference_id,name FROM book ORDER BY book_reference_id;";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new(reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        return result;
    }
}
