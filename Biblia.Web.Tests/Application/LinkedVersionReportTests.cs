using System.Reflection;
using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.BibleDatabases;
using Biblia.Infrastructure.Files;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Biblia.Tests.Application;

public sealed class LinkedVersionReportTests
{
    [Fact]
    public async Task MixedVersionsBelongToEachLinkAndSurvivePreferenceChangesAndThemeEdits()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "Biblia.LinkedVersions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var clock = new Clock();
            var db = new AppDatabase(Path.Combine(root, "app.db"), NullLogger<AppDatabase>.Instance);
            var themes = new ThemeRepository(db, clock);
            var catalog = new BibleVersionCatalogRepository(db);
            var source = CanonicalThemeFlowTests.FindBibles();
            foreach (var code in new[] { "NVI", "ARA", "ACF" })
                await catalog.CreateAsync(new(0, code, code, "pt-BR", code + ".sqlite", Path.Combine(source, code + ".sqlite"), 2,
                    null, null, null, true, true, true, clock.UtcNow, clock.UtcNow, BibleVersionValidationStatus.Compatible, null), ct);
            var manager = DispatchProxy.Create<IBibleVersionManager, CanonicalThemeFlowTests.ManagerProxy>();
            ((CanonicalThemeFlowTests.ManagerProxy)(object)manager).Root = source;
            var bible = new BibleRepository(manager);
            var repo = new SavedReferenceRepository(db, clock);
            var linker = new ThemeVerseLinkService(themes, catalog, bible, repo, NullLogger<ThemeVerseLinkService>.Instance);
            var a = await themes.CreateAsync("QA versões por vínculo", "#1769AA", null, ct);
            var b = await themes.CreateAsync("Segundo tema", "#18864B", null, ct);
            await linker.LinkAsync(a.Id, "NVI", [new(58,12,14,14,"Nota NVI preservada")], false, ct);
            await linker.LinkAsync(a.Id, "ARA", [new(58,10,5,5)], false, ct);
            // Same canonical reference, another theme/version; no duplicate SavedReference.
            await linker.LinkAsync(b.Id, "ACF", [new(58,12,14,14)], true, ct);
            var repeated = await linker.LinkAsync(a.Id, "ARA", [new(58,12,14,14)], true, ct);
            Assert.Equal(1, repeated.AlreadyLinked);
            Assert.Equal(0, repeated.LinksCreated);
            Assert.Equal(2, (await repo.SearchAsync(null, ct)).Count);
            var reference = (await repo.FindCanonicalAsync(58,12,14,14,ct))!;
            var originalLink = (await repo.GetThemeLinksAsync(a.Id, ct)).Single(x => x.ReferenceId == reference.Id);
            await repo.SetThemesAsync(reference.Id, [a.Id,b.Id], ct);
            Assert.Equal(originalLink, (await repo.GetThemeLinksAsync(a.Id, ct)).Single(x => x.ReferenceId == reference.Id));
            var service = new ReportService(repo, themes, catalog, bible, clock);
            var report = await service.BuildThemesAsync(new(a.Id), ct);
            Assert.Equal(new[] { "ARA", "NVI" }, report.Sections.Single().References.Select(x => x.VersionCode));
            Assert.Equal(new[] { 10, 12 }, report.Sections.Single().References.Select(x => x.Chapter));
            foreach (var item in report.Sections.Single().References)
            {
                var passage = await bible.GetPassageAsync(item.VersionCode, 58, item.Chapter, item.VerseStart, item.VerseEnd, ct);
                Assert.Equal(string.Join(" ", passage.Verses.Select(v => $"{v.Verse} {v.Text}")), item.PassageText);
            }
            Assert.Equal("Nota NVI preservada", report.Sections.Single().References[1].Observation);
            var all = await service.BuildThemesAsync(new(null), ct);
            Assert.Equal(3, all.ReferenceCount);
            Assert.Equal("ACF", all.Sections.Single(x => x.Theme.Id == b.Id).References.Single().VersionCode);
            var pdfPath = await new PdfService(new Paths(root)).CreateThemeVersePdfAsync(report, ct);
            using var pdf = PdfDocument.Open(pdfPath);
            var text = string.Join("\n", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
            Assert.Contains("[ARA]", text);
            Assert.Contains("[NVI]", text);
            Assert.DoesNotContain("VERSÃO DA BÍBLIA", text);
            Assert.True(text.IndexOf("Hebreus 10:5", StringComparison.Ordinal) < text.IndexOf("Hebreus 12:14", StringComparison.Ordinal));
            Assert.Contains("Nota NVI preservada", text);
            if (Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR") is string output)
            {
                Directory.CreateDirectory(output);
                File.Copy(pdfPath, Path.Combine(output, "qa-versoes-por-vinculo.pdf"), true);
            }
            var nvi = (await catalog.GetByCodeAsync("NVI", ct))!;
            await catalog.UpdateAsync(nvi with { IsEnabled = false }, ct);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildThemesAsync(new(a.Id), ct));
            Assert.Contains("NVI", error.Message);
            await catalog.UpdateAsync(nvi, ct);
            await using (var connection = await db.OpenConnectionAsync(ct))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE ReferenceTheme SET BibleVersionId=NULL WHERE ReferenceId=$reference AND ThemeId=$theme;";
                command.Parameters.AddWithValue("$reference", reference.Id);
                command.Parameters.AddWithValue("$theme", a.Id);
                await command.ExecuteNonQueryAsync(ct);
            }
            error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildThemesAsync(new(a.Id), ct));
            Assert.Contains("não possui uma versão bíblica registrada", error.Message);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MigrationSnapshotsLegacyPreferenceWithoutInventingMissingVersions()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "Biblia.LinkMigration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "app.db");
            var db = new AppDatabase(path, NullLogger<AppDatabase>.Instance);
            await db.InitializeAsync(ct);
            await using (var connection = await db.OpenConnectionAsync(ct))
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    ALTER TABLE ReferenceTheme DROP COLUMN BibleVersionId;
                    DROP TRIGGER ThemeContent_LinkInserted;
                    DROP TABLE ThemeContent;
                    ALTER TABLE Theme DROP COLUMN OrderingMode;
                    DELETE FROM SchemaMigration WHERE Version>=4;
                    INSERT INTO BibleVersionCatalog(Id,Code,DisplayName,Language,DatabaseFileName,SchemaVersion) VALUES(1,'NVI','NVI','pt-BR','NVI.sqlite',2);
                    INSERT INTO Theme(Id,Name,CreatedAt,UpdatedAt) VALUES(1,'Legado','2026-09-06','2026-09-06');
                    INSERT INTO SavedReference(Id,BookReferenceId,Chapter,VerseStart,VerseEnd,PreferredBibleVersionId,CreatedAt,UpdatedAt) VALUES
                        (1,58,12,14,14,1,'2026-09-06','2026-09-06'),(2,58,10,5,5,NULL,'2026-09-06','2026-09-06');
                    INSERT INTO ReferenceTheme(ReferenceId,ThemeId,Observation) VALUES(1,1,'Nota antiga'),(2,1,NULL);
                    """;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            db = new AppDatabase(path, NullLogger<AppDatabase>.Instance);
            await db.InitializeAsync(ct);
            var repo = new SavedReferenceRepository(db, new Clock());
            var links = await repo.GetThemeLinksAsync(1, ct);
            Assert.Equal(2, links.Count);
            Assert.Equal(1, links.Single(x => x.ReferenceId == 1).BibleVersionId);
            Assert.Equal("Nota antiga", links.Single(x => x.ReferenceId == 1).Observation);
            Assert.Null(links.Single(x => x.ReferenceId == 2).BibleVersionId);
            var reference = (await repo.GetAsync(1, ct))!;
            await repo.UpdateAsync(reference with { PreferredBibleVersionId = null }, ct);
            await new AppDatabase(path, NullLogger<AppDatabase>.Instance).InitializeAsync(ct);
            Assert.Equal(1, (await repo.GetThemeLinksAsync(1, ct)).Single(x => x.ReferenceId == 1).BibleVersionId);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Paths(string root) : IAppPaths
    {
        public string AppDataDirectory => root;
        public string CacheDirectory => root;
        public string GetPrivateFilePath(string name) => Path.Combine(root, name);
    }
}
