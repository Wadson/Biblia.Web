using System.Globalization;
using System.Text.RegularExpressions;
using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Infrastructure.Files;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Biblia.Tests.Infrastructure;

public sealed class ThemeVersePdfServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "BibliaTema.PdfTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T23:35:00Z", CultureInfo.InvariantCulture);
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Theme Theme(int id, string? color = "#336699") => new(id, new[] { "Santidade", "Esperança", "Comunhão" }[(id - 1) % 3], color!, null, Now, Now);
    private static ThemeVerseReportReference Reference(int id, string? observation = null, string? text = null) =>
        new(id, $"João 3:{id}", text ?? $"{id} Deus é amor: ação, fé, bênção e paz — “graça”. MARCADOR{id:D3}.", observation, 43, 3, id, id, "ACF");
    private static ThemeVerseReport Report(params ThemeVerseReportSection[] sections) =>
        new("Temas e versículos", Now, sections.Length, sections.Sum(s => s.References.Count), sections);
    private Task<string> Generate(ThemeVerseReport report) => new PdfService(new TestPaths(root)).CreateThemeVersePdfAsync(report, Ct);
    private static string Text(PdfDocument pdf) => string.Join("\n", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
    private static string Compact(string text) => Regex.Replace(text, @"\s+", "");

    [Fact]
    public async Task SignatureMetadataPortugueseAndSingular()
    {
        var path = await Generate(Report(new ThemeVerseReportSection(Theme(1), [Reference(1, "Observação do vínculo: coração e união.")])));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path, Ct), 0, 4));
        Assert.True(new FileInfo(path).Length > 1000);
        using var pdf = PdfDocument.Open(path);
        Assert.Equal("Temas e versículos", pdf.Information.Title);
        Assert.Equal("BíbliaTema", pdf.Information.Author);
        Assert.Equal("Relatório de temas e versículos", pdf.Information.Subject);
        var text = Text(pdf);
        Assert.Contains("Resumo: 1 Tema | 1 Referência", text);
        Assert.Contains("ação, fé, bênção e paz — “graça”", text);
        Assert.Contains("Observação do vínculo: coração e união.", text);
        Assert.Contains(Now.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR")), text);
        Assert.Contains("[ACF]", text);
        Assert.DoesNotContain("VERSÃO DA BÍBLIA", text);
        Assert.Contains("Página 1 de 1", text);
        Assert.Contains(pdf.GetPage(1).Letters, l => l.Value == "1" && (l.FontName?.Contains("Semi") ?? false));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   \r\n ")]
    public async Task EmptyObservationHasNoBlock(string? observation)
    {
        using var pdf = PdfDocument.Open(await Generate(Report(new ThemeVerseReportSection(Theme(1), [Reference(1, observation)]))));
        Assert.DoesNotContain("OBSERVAÇÃO DO VÍNCULO", Text(pdf));
    }

    [Theory]
    [InlineData("#18864B")] [InlineData(null)] [InlineData("invalid")] [InlineData("#FFFFFF")] [InlineData("#12GG45")]
    public async Task ThemeColorsAndEmptySectionAreSafe(string? color)
    {
        using var pdf = PdfDocument.Open(await Generate(Report(new ThemeVerseReportSection(Theme(1, color), []))));
        Assert.Contains("Nenhum versículo vinculado a este tema.", Text(pdf));
        Assert.Contains("Resumo: 1 Tema | 0 Referências", Text(pdf));
    }

    [Fact]
    public async Task ActualContentOverridesIncorrectDeclaredTotalsAndEveryCardIsPresent()
    {
        var sections = Enumerable.Range(1, 3).Select(t => new ThemeVerseReportSection(Theme(t),
            Enumerable.Range((t - 1) * 10 + 1, 10).Select(i => Reference(i, i % 3 == 0 ? "Observação pastoral: pratique o amor ao próximo." : null)).ToArray())).ToArray();
        var report = Report(sections) with { ThemeCount = 999, ReferenceCount = 25 };
        using var pdf = PdfDocument.Open(await Generate(report));
        var text = Text(pdf);
        Assert.True(pdf.NumberOfPages > 1);
        Assert.Contains("Resumo: 3 Temas | 30 Referências", text);
        Assert.DoesNotContain("999", text);
        for (var i = 1; i <= 30; i++)
        {
            Assert.Single(Regex.Matches(text, $@"João 3:{i}(?!\d)"));
            Assert.Single(Regex.Matches(text, $"MARCADOR{i:D3}"));
        }
        foreach (var page in pdf.GetPages())
        {
            Assert.Contains($"Página {page.Number} de {pdf.NumberOfPages}", page.Text);
            Assert.InRange(page.Width, 595, 596);
            Assert.InRange(page.Height, 841, 843);
            Assert.All(page.Letters, l => { Assert.InRange(l.BoundingBox.Left, 39, 557); Assert.InRange(l.BoundingBox.Bottom, 12, 812); });
            // TOC mentions are links, not body headers requiring a card on that page.
            foreach (var section in sections.Where(s => !page.Text.Contains("SUMÁRIO") && page.Text.Contains(s.Theme.Name)))
                Assert.Contains(section.References, r => page.Text.Contains(r.FormattedReference));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OversizedPassageOrObservationPreservesEveryWord(bool longObservation)
    {
        var longText = string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"palavra{i:D4} ação"));
        var item = Reference(1, longObservation ? longText : "Observação final.", longObservation ? null : longText);
        var path = await Generate(Report(new ThemeVerseReportSection(Theme(1), [item])));
        var output = Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR");
        if (output is not null) { Directory.CreateDirectory(output); File.Copy(path, Path.Combine(output, longObservation ? "qa-observacao-extensa.pdf" : "qa-texto-extenso.pdf"), true); }
        using var pdf = PdfDocument.Open(path);
        var text = Text(pdf);
        Assert.True(pdf.NumberOfPages >= 3);
        Assert.Contains("continuação", text);
        for (var i = 1; i <= 1000; i++) Assert.Single(Regex.Matches(text, $"palavra{i:D4}"));
        foreach (var page in pdf.GetPages())
        {
            Assert.Contains("Santidade", page.Text);
            Assert.All(page.Letters, l => Assert.InRange(l.BoundingBox.Bottom, 12, 812));
        }
    }

    [Fact]
    public async Task LongUnbrokenTextIsNotClipped()
    {
        var value = string.Concat(Enumerable.Repeat("á", 600));
        using var pdf = PdfDocument.Open(await Generate(Report(new ThemeVerseReportSection(Theme(1), [Reference(1, value)]))));
        Assert.Contains(value, Compact(Text(pdf)));
        Assert.All(pdf.GetPages().SelectMany(p => p.Letters), l => Assert.InRange(l.BoundingBox.Right, 39, 557));
    }

    [Fact]
    public async Task LargeTextBlockWrapsAndUsesRemainingPageSpace()
    {
        var content=string.Join(" ",Enumerable.Range(1,500).Select(i=>$"bloco{i:D4} esperança"));
        var block=new ThemeTextBlock(content,new("#172033","#EAF2FF",12,TextMarkerStyle.None,false,false));
        var section=new ThemeVerseReportSection(Theme(1),[Reference(1)],[new(null,block),new(Reference(1),null)],ThemeOrderingMode.Manual);
        using var pdf=PdfDocument.Open(await Generate(Report(section)));
        var text=Text(pdf);
        for(var i=1;i<=500;i++)Assert.Single(Regex.Matches(text,$"bloco{i:D4}"));
        Assert.Contains("João 3:1",text);
        Assert.True(pdf.NumberOfPages>=2);
    }

    [Fact]
    public async Task ConcurrentGenerationUsesDistinctFilesAndEmbeddedFonts()
    {
        var service = new PdfService(new TestPaths(root));
        var report = Report(new ThemeVerseReportSection(Theme(1), [Reference(1)]));
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => service.CreateThemeVersePdfAsync(report, Ct), Ct)));
        Assert.Equal(8, paths.Distinct().Count());
        foreach (var path in paths)
        {
            using var pdf = PdfDocument.Open(path);
            Assert.Contains("bênção", Text(pdf));
            Assert.Matches(@"^temas-versiculos-\d{8}-\d{6}-[a-f0-9]{32}\.pdf$", Path.GetFileName(path));
            using var raw = PdfSharp.Pdf.IO.PdfReader.Open(path, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Assert.Contains(raw.Internals.GetAllObjects(), o => o is PdfSharp.Pdf.PdfDictionary d && d.Elements.ContainsKey("/FontFile2"));
        }
    }

    [Fact]
    public async Task CancellationDoesNotCreateExport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PdfService(new TestPaths(root)).CreateThemeVersePdfAsync(Report(), cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(root, "exports")));
    }

    [Fact]
    public async Task RepresentativeVisualQa()
    {
        var sections = Enumerable.Range(1, 3).Select(t => new ThemeVerseReportSection(Theme(t),
            Enumerable.Range((t - 1) * 10 + 1, 10).Select(i => Reference(i,
                i == 12 ? string.Join(" ", Enumerable.Repeat("A observação pertence ao vínculo. É um convite à reflexão, à esperança e ao cuidado com o próximo.", 16)) :
                i % 4 == 0 ? "SANTUÁRIO: O lugar mais sagrado do templo judaico, onde era mantida a arca da aliança." : null,
                $"{i} Porque Deus amou o mundo de tal maneira que deu o seu Filho unigênito, para que todo aquele que nele crê não pereça, mas tenha a vida eterna. MARCADOR{i:D3}." )).ToArray())).ToArray();
        var path = await Generate(Report(sections));
        var output = Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR");
        if (output is not null) { Directory.CreateDirectory(output); File.Copy(path, Path.Combine(output, "relatorio-qa.pdf"), true); }
        using var pdf = PdfDocument.Open(path);
        Assert.Contains("Resumo: 3 Temas | 30 Referências", Text(pdf));
    }

    [Fact]
    public async Task BlockPrefixesAreUniqueIndentedAndIndependentAcrossConcurrentReports()
    {
        var longText=string.Join("\n",Enumerable.Range(1,95).Select(i=>$"LinhaQA{i:D3} fé e esperança."));
        ThemeReportContentItem Block(string text,TextMarkerStyle marker)=>new(null,new(text,new("#172033","#EAF2FF",12,marker,true,true)));
        ThemeReportContentItem[] mixed=[Block("PrimeiroQA\nSegundaQA",TextMarkerStyle.Numbered),new(Reference(1),null),Block("PontoQA",TextMarkerStyle.Bullet),Block(longText,TextMarkerStyle.OrdinalNumbered),Block("FimQA",TextMarkerStyle.Numbered)];
        var report=Report(new ThemeVerseReportSection(Theme(1),[Reference(1)],mixed,ThemeOrderingMode.Manual));
        var paths=await Task.WhenAll(Generate(report),Generate(report));
        foreach(var path in paths){
            using var pdf=PdfDocument.Open(path);var text=Compact(Text(pdf));
            Assert.Contains("1.PrimeiroQA",text);Assert.Contains("2ºLinhaQA001",text);Assert.Contains("3.FimQA",text);
            Assert.Single(Regex.Matches(text,"2º"));Assert.DoesNotContain("2.SegundaQA",text);
            Assert.True(pdf.NumberOfPages>=3);Assert.True(pdf.TryGetBookmarks(out _));
            for(var i=1;i<=95;i++)Assert.Single(Regex.Matches(text,$"LinhaQA{i:D3}"));
            foreach(var page in pdf.GetPages()){
                Assert.Contains($"Página {page.Number} de {pdf.NumberOfPages}",page.Text);
                Assert.All(page.Letters,l=>{Assert.InRange(l.BoundingBox.Left,39,557);Assert.InRange(l.BoundingBox.Bottom,12,812);});
                var words=page.GetWords().Where(w=>w.Text.StartsWith("LinhaQA")).ToArray();
                if(words.Length>1)Assert.All(words,w=>Assert.InRange(Math.Abs(w.BoundingBox.Left-words[0].BoundingBox.Left),0,.1));
            }
            Assert.Contains(pdf.GetPages().SelectMany(p=>p.Letters),l=>l.Value=="º");
        }
        var output=Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR");
        if(output is not null){Directory.CreateDirectory(output);File.Copy(paths[0],Path.Combine(output,"blocos-multiplas-paginas.pdf"),true);}
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class TestPaths(string root) : IAppPaths
    {
        public string AppDataDirectory => root;
        public string CacheDirectory => root;
        public string GetPrivateFilePath(string fileName) => Path.Combine(root, fileName);
    }
}
