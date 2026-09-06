using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Files;
using Biblia.Infrastructure.Repositories;
using Biblia.Qa;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using Xunit;

namespace Biblia.Tests.Infrastructure;

public sealed class PdfTypographyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Biblia.Typography", Guid.NewGuid().ToString("N"));
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private SettingsService Settings() => new(new SettingsRepository(new AppDatabase(Path.Combine(root, "app.db"), NullLogger<AppDatabase>.Instance), new Clock()));

    [Fact]
    public async Task ExistingDatabaseDefaultsPersistenceAndRestorePreserveOtherSettings()
    {
        var settings = Settings();
        await settings.SetAsync("theme", "dark", Ct);
        Assert.Equal(PdfReportTypographyOptions.Default, await settings.GetPdfTypographyAsync(Ct));
        Assert.Null(await settings.GetAsync(PdfReportSettings.Key, Ct));
        var options = new PdfReportTypographyOptions(12.5, 14.5, 11.5);
        await settings.SavePdfTypographyAsync(options, Ct);
        Assert.Equal(options, await Settings().GetPdfTypographyAsync(Ct));
        await Settings().RestorePdfTypographyAsync(Ct);
        Assert.Equal(PdfReportTypographyOptions.Default, await Settings().GetPdfTypographyAsync(Ct));
        Assert.Equal("dark", await Settings().GetAsync("theme", Ct));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(6.5)] [InlineData(25)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)] [InlineData(10.25)]
    public async Task InvalidValuesAreRejectedWithoutChangingSavedSettings(double invalid)
    {
        var settings = Settings();
        await settings.RestorePdfTypographyAsync(Ct);
        foreach (var value in new[] { new PdfReportTypographyOptions(invalid), new PdfReportTypographyOptions(10, invalid), new PdfReportTypographyOptions(10, 9.5, invalid) })
            await Assert.ThrowsAsync<ArgumentException>(() => settings.SavePdfTypographyAsync(value, Ct));
        Assert.Equal(PdfReportTypographyOptions.Default, await settings.GetPdfTypographyAsync(Ct));
    }

    [Theory]
    [InlineData(7.5, 9.5, 9.5)] [InlineData(24.5, 9.5, 9.5)]
    [InlineData(10, 7.5, 9.5)] [InlineData(10, 22.5, 9.5)]
    [InlineData(10, 9.5, 6.5)] [InlineData(10, 9.5, 20.5)]
    public void IndividualLimitsAreEnforced(double r, double v, double o) =>
        Assert.Throws<ArgumentException>(() => new PdfReportTypographyOptions(r, v, o).Validate());

    [Theory]
    [InlineData("")] [InlineData("texto")] [InlineData("NaN")] [InlineData("null")]
    [InlineData("{\"PdfReferenceFontSize\":\"Infinity\"}")]
    public async Task MalformedStorageUsesDefaults(string raw)
    {
        var settings = Settings();
        await settings.SetAsync(PdfReportSettings.Key, raw, Ct);
        Assert.Equal(PdfReportTypographyOptions.Default, await settings.GetPdfTypographyAsync(Ct));
    }

    [Theory]
    [InlineData(8, 8, 7)] [InlineData(24, 22, 20)] [InlineData(12.5, 10.5, 8.5)]
    [InlineData(24, 9.5, 9.5)] [InlineData(10, 22, 9.5)] [InlineData(10, 9.5, 20)]
    public async Task IndependentSizesLongContentAndContentsAreCorrect(double r, double v, double o)
    {
        var settings = Settings();
        await settings.SavePdfTypographyAsync(new(r, v, o), Ct);
        var report = ThemeReportQaData.Create(12);
        using var pdf = PdfDocument.Open(await new PdfService(new Paths(root), settings).CreateThemeVersePdfAsync(report, Ct));
        ThemeTableOfContentsTests.Validate(pdf, report);
        var words = pdf.GetPages().SelectMany(p => p.GetWords()).ToArray();
        Assert.Contains(words, w => w.Text == "João" && w.Letters.All(l => Math.Abs(l.FontSize - r) < .01));
        Assert.Contains(words, w => w.Text == "sintético" && w.Letters.All(l => Math.Abs(l.FontSize - v) < .01));
        Assert.Contains(words, w => w.Text == "reflexão" && w.Letters.All(l => Math.Abs(l.FontSize - o) < .01));
        var text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
        Assert.DoesNotContain("OBSERVAÇÃO DO VÍNCULO", text);
        Assert.Contains("OBSERVAÇÃO:", text);
        foreach (var item in report.Sections.SelectMany(s => s.References))
            Assert.Contains($"QA{item.SavedReferenceId / 100:D2}REF{item.SavedReferenceId % 100:D2}.", text);
        Assert.Equal(85, System.Text.RegularExpressions.Regex.Matches(text, "perseverança\\.").Count);
        Assert.Contains(pdf.GetPages().SelectMany(p => p.Letters), l => l.Value == "1" && (l.FontName?.Contains("Semi") ?? false) && Math.Abs(l.FontSize - v) < .01);
    }

    [Fact]
    public async Task SameConcurrentServiceReloadsSizesAndRecalculatesPagination()
    {
        var settings = Settings();
        var service = new PdfService(new Paths(root), settings);
        var report = ThemeReportQaData.Create(12);
        await settings.SavePdfTypographyAsync(new(8, 8, 7), Ct);
        using var small = PdfDocument.Open(await service.CreateThemeVersePdfAsync(report, Ct));
        await settings.SavePdfTypographyAsync(new(24, 22, 20), Ct);
        var paths = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => service.CreateThemeVersePdfAsync(report, Ct), Ct)));
        Assert.Equal(4, paths.Distinct().Count());
        foreach (var path in paths)
        {
            using var large = PdfDocument.Open(path);
            Assert.True(large.NumberOfPages > small.NumberOfPages);
            ThemeTableOfContentsTests.Validate(large, report);
        }
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Paths(string root) : IAppPaths
    {
        public string AppDataDirectory => root;
        public string CacheDirectory => root;
        public string GetPrivateFilePath(string name) => Path.Combine(root, name);
    }
}
