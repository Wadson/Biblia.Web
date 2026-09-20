using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;

namespace Biblia.Infrastructure.Repositories;

public sealed class SavedReferenceRepository : SqliteRepositoryBase, ISavedReferenceRepository
{
    private readonly IClock _clock;
    public SavedReferenceRepository(Biblia.Infrastructure.AppDatabase.AppDatabase database, IClock clock) : base(database) => _clock = clock;

    public async Task<SavedReference> CreateAsync(SavedReference item, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var c = await Database.OpenConnectionAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO SavedReference(BookReferenceId,Chapter,VerseStart,VerseEnd,Comment,PreferredBibleVersionId,CreatedAt,UpdatedAt) VALUES($book,$chapter,$start,$end,$comment,$version,$created,$updated); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$book", item.BookReferenceId); cmd.Parameters.AddWithValue("$chapter", item.Chapter); cmd.Parameters.AddWithValue("$start", item.VerseStart); cmd.Parameters.AddWithValue("$end", item.VerseEnd);
        AddNullable(cmd.Parameters, "$comment", item.Comment); AddNullable(cmd.Parameters, "$version", item.PreferredBibleVersionId);
        cmd.Parameters.AddWithValue("$created", now.ToString("O")); cmd.Parameters.AddWithValue("$updated", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        return item with { Id = id, CreatedAt = now, UpdatedAt = now };
    }

    public async Task<SavedReference?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var c = await Database.OpenConnectionAsync(cancellationToken); await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,BookReferenceId,Chapter,VerseStart,VerseEnd,Comment,PreferredBibleVersionId,CreatedAt,UpdatedAt FROM SavedReference WHERE Id=$id;"; cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? new(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetInt64(6),ReadDate(r,7),ReadDate(r,8)) : null;
    }

    public async Task<SavedReferenceDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default)
    {
        var reference = await GetAsync(id, cancellationToken);
        return reference is null ? null : new SavedReferenceDetails(reference, await GetThemesAsync(id, cancellationToken));
    }

    public async Task<SavedReference?> FindCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, CancellationToken cancellationToken = default)
    {
        await using var c = await Database.OpenConnectionAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,BookReferenceId,Chapter,VerseStart,VerseEnd,Comment,PreferredBibleVersionId,CreatedAt,UpdatedAt FROM SavedReference WHERE BookReferenceId=$book AND Chapter=$chapter AND VerseStart=$start AND VerseEnd=$end ORDER BY Id LIMIT 1;";
        cmd.Parameters.AddWithValue("$book", bookReferenceId); cmd.Parameters.AddWithValue("$chapter", chapter); cmd.Parameters.AddWithValue("$start", verseStart); cmd.Parameters.AddWithValue("$end", verseEnd);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<SavedReferenceDetails>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var result = new List<SavedReferenceDetails>();
        var references = new List<SavedReference>();
        await using (var c = await Database.OpenConnectionAsync(cancellationToken))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id,BookReferenceId,Chapter,VerseStart,VerseEnd,Comment,PreferredBibleVersionId,CreatedAt,UpdatedAt
                FROM SavedReference
                WHERE $query IS NULL
                   OR CAST(BookReferenceId AS TEXT) LIKE '%' || $query || '%'
                   OR CAST(Chapter AS TEXT) LIKE '%' || $query || '%'
                   OR COALESCE(Comment, '') LIKE '%' || $query || '%' COLLATE NOCASE
                ORDER BY BookReferenceId,Chapter,VerseStart,VerseEnd,Id;
                """;
            AddNullable(cmd.Parameters, "$query", string.IsNullOrWhiteSpace(query) ? null : query.Trim());
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken)) references.Add(Map(r));
        }
        return await LoadDetailsAsync(references, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedReferenceDetails>> GetByThemeIdsAsync(IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default)
    {
        if (themeIds.Count == 0) return [];
        var ids = themeIds.Distinct().ToArray();
        var references = new List<SavedReference>();
        await using (var connection = await Database.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            var parameters = ids.Select((_, index) => $"$theme{index}").ToArray();
            command.CommandText = $"""
                SELECT DISTINCT r.Id,r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd,r.Comment,r.PreferredBibleVersionId,r.CreatedAt,r.UpdatedAt
                FROM SavedReference r
                INNER JOIN ReferenceTheme rt ON rt.ReferenceId=r.Id
                WHERE rt.ThemeId IN ({string.Join(',', parameters)})
                ORDER BY r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd,r.Id;
                """;
            for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue(parameters[index], ids[index]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) references.Add(Map(reader));
        }
        return await LoadDetailsAsync(references, cancellationToken);
    }

    public async Task UpdateAsync(SavedReference item, CancellationToken cancellationToken = default)
    {
        await using var c = await Database.OpenConnectionAsync(cancellationToken); await using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE SavedReference SET BookReferenceId=$book,Chapter=$chapter,VerseStart=$start,VerseEnd=$end,Comment=$comment,PreferredBibleVersionId=$version,UpdatedAt=$updated WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$book",item.BookReferenceId); cmd.Parameters.AddWithValue("$chapter",item.Chapter); cmd.Parameters.AddWithValue("$start",item.VerseStart); cmd.Parameters.AddWithValue("$end",item.VerseEnd); AddNullable(cmd.Parameters,"$comment",item.Comment); AddNullable(cmd.Parameters,"$version",item.PreferredBibleVersionId); cmd.Parameters.AddWithValue("$updated",_clock.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$id",item.Id);
        if(await cmd.ExecuteNonQueryAsync(cancellationToken)!=1) throw new KeyNotFoundException("Referência não encontrada.");
    }

    public Task AddThemeAsync(long referenceId,long themeId,CancellationToken cancellationToken=default) => ExecuteLinkAsync("INSERT INTO ReferenceTheme(ReferenceId,ThemeId,BibleVersionId) VALUES($reference,$theme,(SELECT PreferredBibleVersionId FROM SavedReference WHERE Id=$reference));",referenceId,themeId,cancellationToken);
    public Task RemoveThemeAsync(long referenceId,long themeId,CancellationToken cancellationToken=default) => ExecuteLinkAsync("DELETE FROM ReferenceTheme WHERE ReferenceId=$reference AND ThemeId=$theme;",referenceId,themeId,cancellationToken);
    public async Task SetThemesAsync(long referenceId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default)
    {
        await using var c = await Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = c.BeginTransaction();
        try
        {
            await using var delete = c.CreateCommand();
            delete.Transaction = transaction;
            var retained = themeIds.Distinct().ToArray();
            var parameters = retained.Select((_, i) => "$keep" + i).ToArray();
            delete.CommandText = "DELETE FROM ReferenceTheme WHERE ReferenceId=$reference" +
                (retained.Length == 0 ? ";" : $" AND ThemeId NOT IN ({string.Join(',', parameters)});");
            for (var i = 0; i < retained.Length; i++) delete.Parameters.AddWithValue(parameters[i], retained[i]);
            delete.Parameters.AddWithValue("$reference", referenceId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            foreach (var themeId in themeIds.Distinct())
            {
                await using var insert = c.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO ReferenceTheme(ReferenceId,ThemeId,BibleVersionId) SELECT $reference,$theme,PreferredBibleVersionId FROM SavedReference WHERE Id=$reference;";
                insert.Parameters.AddWithValue("$reference", referenceId);
                insert.Parameters.AddWithValue("$theme", themeId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<LinkVersesToThemeResult> LinkBatchToThemeAsync(long themeId, long preferredVersionId, IReadOnlyCollection<VerseSelection> selections, bool replaceExistingPreferredVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var created = 0; var reused = 0; var linked = 0; var alreadyLinked = 0;
        try
        {
            foreach (var selection in selections)
            {
                long referenceId;
                long? currentPreferred;
                await using (var find = connection.CreateCommand())
                {
                    find.Transaction = transaction;
                    find.CommandText = "SELECT Id,PreferredBibleVersionId FROM SavedReference WHERE BookReferenceId=$book AND Chapter=$chapter AND VerseStart=$start AND VerseEnd=$end ORDER BY Id LIMIT 1;";
                    find.Parameters.AddWithValue("$book", selection.BookReferenceId); find.Parameters.AddWithValue("$chapter", selection.Chapter); find.Parameters.AddWithValue("$start", selection.VerseStart); find.Parameters.AddWithValue("$end", selection.VerseEnd);
                    await using var reader = await find.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken)) { referenceId = reader.GetInt64(0); currentPreferred = reader.IsDBNull(1) ? null : reader.GetInt64(1); reused++; }
                    else { referenceId = 0; currentPreferred = null; }
                }
                if (referenceId == 0)
                {
                    await using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO SavedReference(BookReferenceId,Chapter,VerseStart,VerseEnd,Comment,PreferredBibleVersionId,CreatedAt,UpdatedAt) VALUES($book,$chapter,$start,$end,NULL,$version,$now,$now); SELECT last_insert_rowid();";
                    insert.Parameters.AddWithValue("$book", selection.BookReferenceId); insert.Parameters.AddWithValue("$chapter", selection.Chapter); insert.Parameters.AddWithValue("$start", selection.VerseStart); insert.Parameters.AddWithValue("$end", selection.VerseEnd); insert.Parameters.AddWithValue("$version", preferredVersionId); insert.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
                    referenceId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken)); created++;
                }
                else if (currentPreferred is null || replaceExistingPreferredVersion)
                {
                    await using var update = connection.CreateCommand(); update.Transaction = transaction;
                    update.CommandText = "UPDATE SavedReference SET PreferredBibleVersionId=$version,UpdatedAt=$now WHERE Id=$id;";
                    update.Parameters.AddWithValue("$version", preferredVersionId); update.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O")); update.Parameters.AddWithValue("$id", referenceId);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }
                var observation = CleanObservation(selection.Observation);
                await using var link = connection.CreateCommand(); link.Transaction = transaction;
                link.CommandText = "INSERT OR IGNORE INTO ReferenceTheme(ReferenceId,ThemeId,Observation,CreatedAt,UpdatedAt,BibleVersionId) VALUES($reference,$theme,$observation,$now,$now,$version);";
                link.Parameters.AddWithValue("$version", preferredVersionId);
                link.Parameters.AddWithValue("$reference", referenceId); link.Parameters.AddWithValue("$theme", themeId);
                AddNullable(link.Parameters, "$observation", observation);
                link.Parameters.AddWithValue("$now", _clock.UtcNow.ToString("O"));
                if (await link.ExecuteNonQueryAsync(cancellationToken) == 1) linked++;
                else
                {
                    alreadyLinked++;
                    // A referência pode já pertencer ao tema (inclusive por outra
                    // publicação). Nesse caso, a observação informada nesta nova
                    // vinculação precisa ser persistida, e não silenciosamente ignorada.
                    if (observation is not null)
                    {
                        await using var updateObservation = connection.CreateCommand();
                        updateObservation.Transaction = transaction;
                        updateObservation.CommandText = "UPDATE ReferenceTheme SET Observation=$observation,UpdatedAt=$updated WHERE ReferenceId=$reference AND ThemeId=$theme;";
                        AddNullable(updateObservation.Parameters, "$observation", observation);
                        updateObservation.Parameters.AddWithValue("$updated", _clock.UtcNow.ToString("O"));
                        updateObservation.Parameters.AddWithValue("$reference", referenceId);
                        updateObservation.Parameters.AddWithValue("$theme", themeId);
                        await updateObservation.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return new(selections.Count, created, reused, linked, alreadyLinked);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<IReadOnlyList<ReferenceTheme>> GetThemeLinksAsync(long themeId, CancellationToken cancellationToken = default)
    {
        var result = new List<ReferenceTheme>();
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rt.ReferenceId,rt.ThemeId,rt.Observation,rt.CreatedAt,rt.UpdatedAt,rt.BibleVersionId
            FROM ReferenceTheme rt
            INNER JOIN SavedReference r ON r.Id=rt.ReferenceId
            WHERE rt.ThemeId=$theme
            ORDER BY r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd,r.Id;
            """;
        command.Parameters.AddWithValue("$theme", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var created = reader.IsDBNull(3) ? DateTimeOffset.UnixEpoch : DateTimeOffset.Parse(reader.GetString(3));
            var updated = reader.IsDBNull(4) ? created : DateTimeOffset.Parse(reader.GetString(4));
            result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2), created, updated, reader.IsDBNull(5) ? null : reader.GetInt64(5)));
        }
        return result;
    }

    public async Task UpdateThemeObservationAsync(long referenceId, long themeId, string? observation, CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ReferenceTheme SET Observation=$observation,UpdatedAt=$updated WHERE ReferenceId=$reference AND ThemeId=$theme;";
        AddNullable(command.Parameters, "$observation", CleanObservation(observation));
        command.Parameters.AddWithValue("$updated", _clock.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$reference", referenceId);
        command.Parameters.AddWithValue("$theme", themeId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new KeyNotFoundException("Vínculo entre tema e referência não encontrado.");
    }

    public async Task<int> CountThemeLinksAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReferenceTheme;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountReferencesWithCommentsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM SavedReference r
            WHERE TRIM(COALESCE(r.Comment, '')) <> ''
               OR EXISTS (
                   SELECT 1
                   FROM ReferenceTheme rt
                   WHERE rt.ReferenceId = r.Id
                     AND TRIM(COALESCE(rt.Observation, '')) <> ''
               );
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string? CleanObservation(string? value)
    {
        var cleaned = value?.Trim();
        if (cleaned?.Length > 2000) throw new InvalidOperationException("A observação deve ter no máximo 2.000 caracteres.");
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
    private async Task ExecuteLinkAsync(string sql,long referenceId,long themeId,CancellationToken token){await using var c=await Database.OpenConnectionAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$reference",referenceId);cmd.Parameters.AddWithValue("$theme",themeId);await cmd.ExecuteNonQueryAsync(token);}

    public async Task DeleteAsync(long id,CancellationToken cancellationToken=default){await using var c=await Database.OpenConnectionAsync(cancellationToken);await using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM SavedReference WHERE Id=$id;";cmd.Parameters.AddWithValue("$id",id);await cmd.ExecuteNonQueryAsync(cancellationToken);}

    private async Task<IReadOnlyList<Theme>> GetThemesAsync(long referenceId, CancellationToken token)
    {
        await using var c = await Database.OpenConnectionAsync(token);
        return await GetThemesAsync(c, referenceId, token);
    }

    private static async Task<IReadOnlyList<Theme>> GetThemesAsync(Microsoft.Data.Sqlite.SqliteConnection c, long referenceId, CancellationToken token)
    {
        var themes = new List<Theme>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT t.Id,t.Name,t.ColorHex,t.Description,t.CreatedAt,t.UpdatedAt FROM Theme t INNER JOIN ReferenceTheme rt ON rt.ThemeId=t.Id WHERE rt.ReferenceId=$reference ORDER BY t.Name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$reference", referenceId);
        await using var r = await cmd.ExecuteReaderAsync(token);
        while (await r.ReadAsync(token)) themes.Add(new Theme(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), ReadDate(r, 4), ReadDate(r, 5)));
        return themes;
    }

    private async Task<IReadOnlyList<SavedReferenceDetails>> LoadDetailsAsync(IReadOnlyList<SavedReference> references, CancellationToken token)
    {
        if (references.Count == 0) return [];
        var themesByReference = references.ToDictionary(x => x.Id, _ => new List<Theme>());
        await using var connection = await Database.OpenConnectionAsync(token);
        await using var command = connection.CreateCommand();
        var names = references.Select((_, index) => $"$id{index}").ToArray();
        command.CommandText = $"SELECT rt.ReferenceId,t.Id,t.Name,t.ColorHex,t.Description,t.CreatedAt,t.UpdatedAt FROM ReferenceTheme rt INNER JOIN Theme t ON t.Id=rt.ThemeId WHERE rt.ReferenceId IN ({string.Join(',', names)}) ORDER BY t.Name COLLATE NOCASE;";
        for (var i = 0; i < references.Count; i++) command.Parameters.AddWithValue(names[i], references[i].Id);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) themesByReference[reader.GetInt64(0)].Add(new(reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), ReadDate(reader, 5), ReadDate(reader, 6)));
        return references.Select(x => new SavedReferenceDetails(x, themesByReference[x.Id])).ToArray();
    }

    private static SavedReference Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetInt64(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),r.GetInt32(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetInt64(6),ReadDate(r,7),ReadDate(r,8));
}
