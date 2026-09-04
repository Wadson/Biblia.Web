using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biblia.Tests.Integration;

#pragma warning disable xUnit1051
public sealed class ThemeVerseLinkRepositoryTests
{
    [Fact]
    public async Task Batch_CreatesReusesAvoidsDuplicatesAndUnlinkPreservesReference()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BibliaTema.LinkTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clock = new FixedClock(DateTimeOffset.Parse("2026-09-03T20:00:00Z"));
            var database = new AppDatabase(Path.Combine(folder, "app.db"), NullLogger<AppDatabase>.Instance); await database.InitializeAsync();
            var themes = new ThemeRepository(database, clock); var firstTheme = await themes.CreateAsync("Santidade", "#1769AA", null); var secondTheme = await themes.CreateAsync("Graça", "#18864B", null);
            var versions = new BibleVersionCatalogRepository(database); var firstVersion = await versions.CreateAsync(Version("ACF")); var secondVersion = await versions.CreateAsync(Version("NVI"));
            var repository = new SavedReferenceRepository(database, clock);
            var existing = await repository.CreateAsync(new(0, 43, 3, 16, 16, null, firstVersion.Id, default, default)); await repository.AddThemeAsync(existing.Id, secondTheme.Id);

            var result = await repository.LinkBatchToThemeAsync(firstTheme.Id, secondVersion.Id, [new(43,3,16,16), new(60,1,15,16)], false);
            Assert.Equal(new LinkVersesToThemeResult(2, 1, 1, 2, 0), result);
            Assert.Equal(firstVersion.Id, (await repository.GetAsync(existing.Id))!.PreferredBibleVersionId);

            var repeated = await repository.LinkBatchToThemeAsync(firstTheme.Id, secondVersion.Id, [new(43,3,16,16), new(60,1,15,16)], true);
            Assert.Equal(0, repeated.LinksCreated); Assert.Equal(2, repeated.AlreadyLinked);
            Assert.Equal(secondVersion.Id, (await repository.GetAsync(existing.Id))!.PreferredBibleVersionId);

            await repository.RemoveThemeAsync(existing.Id, firstTheme.Id);
            var preserved = await repository.GetDetailsAsync(existing.Id);
            Assert.NotNull(preserved); Assert.Single(preserved!.Themes); Assert.Equal("Graça", preserved.Themes[0].Name);

            await using var connection = await database.OpenConnectionAsync();
            await using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check;"; Assert.Equal("ok", await integrity.ExecuteScalarAsync());
            await using var foreignKeys = connection.CreateCommand(); foreignKeys.CommandText = "PRAGMA foreign_key_check;"; await using var reader = await foreignKeys.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Batch_RollsBackEveryItemWhenForeignKeyFails()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BibliaTema.LinkTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var clock = new FixedClock(DateTimeOffset.UtcNow); var database = new AppDatabase(Path.Combine(folder, "app.db"), NullLogger<AppDatabase>.Instance); await database.InitializeAsync();
            var version = await new BibleVersionCatalogRepository(database).CreateAsync(Version("ACF")); var repository = new SavedReferenceRepository(database, clock);
            await Assert.ThrowsAsync<SqliteException>(() => repository.LinkBatchToThemeAsync(999, version.Id, [new(43,3,16,16), new(1,1,1,1)], false));
            Assert.Empty(await repository.SearchAsync(null));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static BibleVersionCatalogEntry Version(string code) => new(0, code, code, "pt-BR", $"{code}.sqlite", $"{code}.sqlite", 2, null, null, null, true, true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BibleVersionValidationStatus.Compatible, null);
    private sealed class FixedClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow { get; } = value; }
}
#pragma warning restore xUnit1051
