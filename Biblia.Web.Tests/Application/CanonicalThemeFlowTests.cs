using System.Reflection;
using System.Text.RegularExpressions;
using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.Rules;
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

public sealed class CanonicalThemeFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReverseAndRandomLinkingSurviveReloadEditUnlinkVersionChangeAndPdf(bool random)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "Biblia.Canonical", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = FindBibles();
            var clock = new Clock();
            var database = new AppDatabase(Path.Combine(root, "app.db"), NullLogger<AppDatabase>.Instance);
            await database.InitializeAsync(ct);
            var themes = new ThemeRepository(database, clock);
            var versions = new BibleVersionCatalogRepository(database);
            foreach (var code in new[] { "ACF", "NVI" })
                await versions.CreateAsync(new(0, code, code, "pt-BR", code + ".sqlite", Path.Combine(source, code + ".sqlite"), 2,
                    null, null, null, true, true, true, clock.UtcNow, clock.UtcNow, BibleVersionValidationStatus.Compatible, null), ct);
            var manager = DispatchProxy.Create<IBibleVersionManager, ManagerProxy>();
            ((ManagerProxy)(object)manager).Root = source;
            var bible = new BibleRepository(manager);
            var references = new SavedReferenceRepository(database, clock);
            ThemeVerseLinkService LinkService() => new(themes, versions, bible, references, NullLogger<ThemeVerseLinkService>.Instance);
            var links = LinkService();
            var selections = Enumerable.Range(1, 66).Reverse().Select(b => new VerseSelection(b, 1, 1, 1, b % 2 == 0 ? $"Nota do livro {b}" : null)).ToArray();
            if (random) new Random(20260906).Shuffle(selections);
            var first = await themes.CreateAsync("QA cânon A", "#1769AA", null, ct);
            var second = await themes.CreateAsync("QA cânon B", "#18864B", null, ct);
            foreach (var theme in new[] { first, second })
            {
                await links.LinkAsync(theme.Id, "ACF", selections, false, ct);
                await links.LinkAsync(theme.Id, "ACF", selections, false, ct);
                var immediate = await links.GetLinkedAsync(theme.Id, "ACF", ct);
                Assert.Equal(Enumerable.Range(1, 66), immediate.Select(r => r.BookReferenceId));
                Assert.Equal(immediate, await LinkService().GetLinkedAsync(theme.Id, "ACF", ct));
                var john = immediate.Single(r => r.BookReferenceId == 43);
                await links.UpdateObservationAsync(theme.Id, john.ReferenceId, "Nota editada de João", ct);
                var edited = await links.GetLinkedAsync(theme.Id, "ACF", ct);
                Assert.Equal(immediate.Select(r => r.ReferenceId), edited.Select(r => r.ReferenceId));
                Assert.Equal("Nota editada de João", edited[42].Observation);
                await links.UnlinkAsync(theme.Id, john.ReferenceId, ct);
                Assert.Equal(Enumerable.Range(1, 66).Except([43]), (await links.GetLinkedAsync(theme.Id, "ACF", ct)).Select(r => r.BookReferenceId));
                await links.LinkAsync(theme.Id, "ACF", [new(43, 1, 1, 1, "Nota editada de João")], false, ct);
                Assert.Equal(Enumerable.Range(1, 66), (await links.GetLinkedAsync(theme.Id, "NVI", ct)).Select(r => r.BookReferenceId));
            }

            // Independent numeric expectations, including equal starts, ranges, and stable IDs.
            await links.LinkAsync(first.Id, "ACF", [new(43,10,2,2), new(43,2,10,10), new(43,3,10,10), new(43,3,2,4), new(43,3,2,2)], false, ct);
            var duplicate = await references.CreateAsync(new(0,43,3,2,2,null,(await versions.GetByCodeAsync("ACF", ct))!.Id,default,default), ct);
            await references.AddThemeAsync(duplicate.Id, first.Id, ct);
            var johns = (await links.GetLinkedAsync(first.Id, "ACF", ct)).Where(r => r.BookReferenceId == 43).ToArray();
            Assert.Equal(new[] { (1,1,1), (2,10,10), (3,2,2), (3,2,2), (3,2,4), (3,10,10), (10,2,2) }, johns.Select(r => (r.Chapter,r.Verse,r.VerseEnd)));
            Assert.True(johns[2].ReferenceId < johns[3].ReferenceId);
            Assert.Equal("João 3:2–4", johns[4].FormattedReference);
            var saved = await new SavedReferenceService(references).SearchAsync(null, ct);
            Assert.Equal(1, saved[0].Reference.BookReferenceId);
            Assert.Equal(66, saved[^1].Reference.BookReferenceId);
            var queried = await references.GetByThemeIdsAsync([first.Id, second.Id], ct);
            Assert.Equal(saved.Select(r => r.Reference.Id), queried.Select(r => r.Reference.Id));

            var report = await new ReportService(references, themes, versions, bible, clock).BuildThemesAsync(new(null), ct);
            Assert.Equal(138, report.ReferenceCount);
            foreach (var section in report.Sections)
                Assert.Equal(Enumerable.Range(1,66), section.References.Select(r => r.BookReferenceId).Distinct());
            var path = await new PdfService(new Paths(root)).CreateThemeVersePdfAsync(report, ct);
            using var pdf = PdfDocument.Open(path);
            Assert.True(pdf.NumberOfPages > 4);
            var text = Regex.Replace(string.Join("\n", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p))), @"\s+", " ");
            var cursor = 0;
            foreach (var section in report.Sections)
                foreach (var item in section.References)
                {
                    var position = text.IndexOf(item.FormattedReference, cursor, StringComparison.Ordinal);
                    Assert.True(position >= cursor, $"Referência fora da sequência: {item.FormattedReference}");
                    cursor = position + item.FormattedReference.Length;
                    if (item.Observation is not null)
                    {
                        var note = text.IndexOf(item.Observation, cursor, StringComparison.Ordinal);
                        Assert.True(note >= cursor);
                        cursor = note + item.Observation.Length;
                    }
                }
            Assert.Contains("138 Referências", text);
            foreach (var page in pdf.GetPages()) Assert.Contains($"Página {page.Number} de {pdf.NumberOfPages}", page.Text);
            if (!random && Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR") is string output)
            {
                Directory.CreateDirectory(output);
                File.Copy(path, Path.Combine(output, "qa-ordem-canonica-66-livros.pdf"), true);
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    internal static string FindBibles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "Biblia.Web", "Content", "Bibles");
            if (Directory.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Bancos de teste do projeto não encontrados.");
    }

    public class ManagerProxy : DispatchProxy
    {
        public string Root { get; set; } = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "ResolveDatabasePathAsync"
            ? Task.FromResult(Path.Combine(Root, args![0] + ".sqlite")) : throw new NotSupportedException(method.Name);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-06T15:00:00Z"); }
    private sealed class Paths(string root) : IAppPaths
    {
        public string AppDataDirectory => root;
        public string CacheDirectory => root;
        public string GetPrivateFilePath(string name) => Path.Combine(root, name);
    }
}


