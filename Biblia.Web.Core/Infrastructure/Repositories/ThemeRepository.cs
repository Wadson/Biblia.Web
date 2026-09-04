using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Exceptions;
using Microsoft.Data.Sqlite;

namespace Biblia.Infrastructure.Repositories;

public sealed class ThemeRepository : SqliteRepositoryBase, IThemeRepository
{
    private readonly IClock _clock;
    public ThemeRepository(Biblia.Infrastructure.AppDatabase.AppDatabase database, IClock clock) : base(database) => _clock = clock;

    public async Task<Theme> CreateAsync(string name, string? colorHex, string? description, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var now = _clock.UtcNow;
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Theme(Name,ColorHex,Description,CreatedAt,UpdatedAt) VALUES($name,$color,$description,$created,$updated); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$name", name.Trim());
        AddNullable(command.Parameters, "$color", colorHex);
        AddNullable(command.Parameters, "$description", description);
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        long id;
        try
        {
            id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch (SqliteException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DomainValidationException("Já existe um tema com esse nome.");
        }
        return new Theme(id, name.Trim(), colorHex, description, now, now);
    }

    public async Task<Theme?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,ColorHex,Description,CreatedAt,UpdatedAt FROM Theme WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Theme>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Theme>();
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,ColorHex,Description,CreatedAt,UpdatedAt FROM Theme ORDER BY Name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    public async Task<IReadOnlyList<Theme>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var result = new List<Theme>();
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,Name,ColorHex,Description,CreatedAt,UpdatedAt
            FROM Theme
            WHERE Name LIKE '%' || $query || '%' COLLATE NOCASE
               OR COALESCE(Description, '') LIKE '%' || $query || '%' COLLATE NOCASE
            ORDER BY Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$query", query.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Map(reader));
        return result;
    }

    public async Task<ThemeUsage> GetUsageAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReferenceTheme WHERE ThemeId=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new ThemeUsage(0);
        return new ThemeUsage(reader.GetInt32(0));
    }

    public async Task UpdateAsync(Theme theme, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme.Name);
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Theme SET Name=$name,ColorHex=$color,Description=$description,UpdatedAt=$updated WHERE Id=$id;";
        command.Parameters.AddWithValue("$name", theme.Name.Trim());
        AddNullable(command.Parameters, "$color", theme.ColorHex);
        AddNullable(command.Parameters, "$description", theme.Description);
        command.Parameters.AddWithValue("$updated", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", theme.Id);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new KeyNotFoundException("Tema não encontrado.");
        }
        catch (SqliteException ex) when (IsUniqueNameViolation(ex))
        {
            throw new DomainValidationException("Já existe um tema com esse nome.");
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Theme WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Theme Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), ReadDate(r, 4), ReadDate(r, 5));
    private static bool IsUniqueNameViolation(SqliteException exception) =>
        exception.SqliteErrorCode == 19 &&
        (exception.SqliteExtendedErrorCode == 2067 || exception.Message.Contains("Theme.Name", StringComparison.OrdinalIgnoreCase));
}
