using System.Globalization;
using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Infrastructure.Files;
using Biblia.Qa;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Outline;
using Xunit;

namespace Biblia.Tests.Infrastructure;

public sealed class ThemeTableOfContentsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "BibliaTema.TocTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1)] [InlineData(5)] [InlineData(50)]
    public async Task ContentsNumbersLinksAndBookmarksMatchActualHeaders(int themeCount)
    {
        var report = ThemeReportQaData.Create(themeCount);
        var path = await Generate(report);
        using var pdf = PdfDocument.Open(path);
        Validate(pdf, report);
        var tocPages = pdf.GetPages().Where(p => p.GetAnnotations().Any()).ToArray();
        Assert.Equal(themeCount == 50, tocPages.Length > 1);
        if (themeCount == 50)
        {
            Assert.True(pdf.NumberOfPages >= 30);
            var output = Environment.GetEnvironmentVariable("BIBLIATEMA_TOC_QA_DIR");
            if (output is not null) { Directory.CreateDirectory(output); File.Copy(path, Path.Combine(output, "relatorio-sumario-qa.pdf"), true); }
        }
        if (themeCount == 1)
        {
            Assert.True(pdf.TryGetBookmarks(out var bookmarks));
            Assert.Equal(1, Assert.IsType<DocumentBookmarkNode>(Assert.Single(bookmarks.Roots)).PageNumber);
        }
    }

    [Fact]
    public async Task DuplicateNamesAndIdsHaveIndependentDestinations()
    {
        var original = ThemeReportQaData.Create(3);
        var report = original with { Sections = original.Sections.Select(s => s with
            { Theme = s.Theme with { Id = 15, Name = "Fé & família / ação — “graça”" } }).ToArray() };
        using var pdf = PdfDocument.Open(await Generate(report));
        Validate(pdf, report);
    }

    [Fact]
    public async Task PageReferencesRecalculateWhenContentAndContentsGrow()
    {
        var small = ThemeReportQaData.Create(2);
        using var before = PdfDocument.Open(await Generate(small));
        var expanded = ThemeReportQaData.Create(50);
        using var after = PdfDocument.Open(await Generate(expanded));
        Validate(before, small);
        Validate(after, expanded);
        Assert.True(before.TryGetBookmarks(out var first));
        Assert.True(after.TryGetBookmarks(out var second));
        Assert.True(((DocumentBookmarkNode)second.Roots[1]).PageNumber > ((DocumentBookmarkNode)first.Roots[1]).PageNumber);
    }

    [Fact]
    public async Task SingleReferenceUsesSingularInContents()
    {
        var original = ThemeReportQaData.Create(1);
        var report = original with { Sections = [original.Sections[0] with { References = original.Sections[0].References.Take(1).ToArray() }] };
        using var pdf = PdfDocument.Open(await Generate(report));
        Validate(pdf, report);
        Assert.Contains("1 referência", pdf.GetPage(1).Text);
    }

    [Fact]
    public async Task EmptyReportHasAnEmptyContentsState()
    {
        using var pdf = PdfDocument.Open(await Generate(ThemeReportQaData.Create(0)));
        Assert.Contains("SUMÁRIO", pdf.GetPage(1).Text);
        Assert.Contains("Nenhum tema incluído no relatório.", pdf.GetPage(1).Text);
        Assert.Empty(pdf.GetPage(1).GetAnnotations());
    }

    internal static void Validate(PdfDocument pdf, ThemeVerseReport report)
    {
        Assert.True(pdf.TryGetBookmarks(out var bookmarks));
        Assert.Equal(report.Sections.Count, bookmarks.Roots.Count);
        var entries = pdf.GetPages().SelectMany(p => p.GetAnnotations().Select(a => (Page: p, Link: a))).ToArray();
        Assert.Equal(report.Sections.Count, entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var section = report.Sections[i];
            var (tocPage, link) = entries[i];
            var action = Assert.IsAssignableFrom<AbstractGoToAction>(link.Action);
            var bookmark = Assert.IsType<DocumentBookmarkNode>(bookmarks.Roots[i]);
            Assert.Equal(section.Theme.Name, bookmark.Title);
            Assert.Equal(bookmark.PageNumber, action.Destination.PageNumber);
            // PDFsharp serializes outline coordinates at two decimals, named destinations at full precision.
            Assert.InRange(Math.Abs(bookmark.Destination.Coordinates.Top!.Value - action.Destination.Coordinates.Top!.Value), 0, .01);
            var target = pdf.GetPage(bookmark.PageNumber);
            var top = action.Destination.Coordinates.Top!.Value;
            // Compare with the actual 16pt theme heading glyphs, not another page counter.
            var headingLetters = target.Letters.Where(l => Math.Abs(l.FontSize - 16) < .1 &&
                l.BoundingBox.Top <= top + 1 && l.BoundingBox.Top > top - 23).ToArray();
            Assert.NotEmpty(headingLetters);
            Assert.InRange(top - headingLetters.Max(l => l.BoundingBox.Top), 0, 12);
            Assert.StartsWith(new string(headingLetters.SelectMany(l => l.Value).ToArray()).Trim(), section.Theme.Name);
            var region = tocPage.Letters.Where(l => l.BoundingBox.Left >= link.Rectangle.Left - 1 &&
                l.BoundingBox.Right <= link.Rectangle.Right + 1 && l.BoundingBox.Bottom >= link.Rectangle.Bottom &&
                l.BoundingBox.Top <= link.Rectangle.Top).ToArray();
            var number = string.Concat(region.Where(l => l.BoundingBox.Left > link.Rectangle.Right - 36).Select(l => l.Value));
            Assert.Equal(bookmark.PageNumber.ToString(CultureInfo.InvariantCulture), number);
            var count = $"{section.References.Count} {(section.References.Count == 1 ? "referência" : "referências")}";
            Assert.Contains(count, string.Concat(region.Select(l => l.Value)));
            Assert.Equal(section.Theme.Name.Replace(" ", ""), string.Concat(region.Where(l => Math.Abs(l.FontSize - 10) < .1).Select(l => l.Value)).Replace(" ", ""));
        }
        foreach (var page in pdf.GetPages())
        {
            Assert.Contains($"Página {page.Number} de {pdf.NumberOfPages}", page.Text);
            var left = page.Number % 2 == 1 ? 58 : 40;
            Assert.All(page.Letters, l =>
            {
                Assert.InRange(l.BoundingBox.Left, left - 2, left + 498);
                Assert.InRange(l.BoundingBox.Right, left - 2, left + 498);
                Assert.InRange(l.BoundingBox.Bottom, 12, 804);
            });
        }
    }

    private Task<string> Generate(ThemeVerseReport report) => new PdfService(new TestPaths(root)).CreateThemeVersePdfAsync(report, TestContext.Current.CancellationToken);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class TestPaths(string root) : IAppPaths
    {
        public string AppDataDirectory => root;
        public string CacheDirectory => root;
        public string GetPrivateFilePath(string name) => Path.Combine(root, name);
    }
}
