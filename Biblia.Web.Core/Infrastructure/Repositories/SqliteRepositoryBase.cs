using Biblia.Infrastructure.AppDatabase;
using Microsoft.Data.Sqlite;

namespace Biblia.Infrastructure.Repositories;

public abstract class SqliteRepositoryBase
{
    protected SqliteRepositoryBase(Biblia.Infrastructure.AppDatabase.AppDatabase database) => Database = database;
    protected Biblia.Infrastructure.AppDatabase.AppDatabase Database { get; }

    protected static void AddNullable(SqliteParameterCollection parameters, string name, object? value) =>
        parameters.AddWithValue(name, value ?? DBNull.Value);

    protected static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);

    protected static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);
}
