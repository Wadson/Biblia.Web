using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Biblia.Infrastructure.Files;

public sealed class PdfService : IPdfService
{
    private static readonly object FontLock = new();
    private readonly IAppPaths paths;
    private readonly ISettingsService? settings;

    public PdfService(IAppPaths paths, ISettingsService? settings = null)
    {
        this.paths = paths;
        this.settings = settings;
        lock (FontLock)
            GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
    }

    public async Task<string> CreateThemeVersePdfAsync(ThemeVerseReport report, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var typography = settings is null ? PdfReportTypographyOptions.Default : await settings.GetPdfTypographyAsync(ct);
        var directory = Path.Combine(paths.CacheDirectory, "exports");
        Directory.CreateDirectory(directory);
        // A separate file per invocation also prevents concurrent downloads being overwritten.
        var path = Path.Combine(directory, $"temas-versiculos-{report.GeneratedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();
        document.Info.Title = report.Title;
        document.Info.Subject = "Relatório de temas e versículos";
        document.Info.Author = "BíbliaTema";
        using (var layout = new ThemeReportLayout(document, ct, typography))
            layout.Render(report);
        ct.ThrowIfCancellationRequested();
        try
        {
            document.Save(path);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return path;
    }
}

// PDFsharp is the existing PDF engine. Its measured vector layout allows rounded cards
// and safe splitting of oversized content, which cannot be split inside a MigraDoc table row.
internal sealed class ThemeReportLayout(PdfDocument document, CancellationToken ct, PdfReportTypographyOptions typography) : IDisposable
{
    private const double TopMargin = 40, InnerMargin = 58, OuterMargin = 40, Bottom = 795;
    private const double Width = 497.28, TocNumberWidth = 36, TocEntryGap = 10, TocHeadingHeight = 35;
    private double Margin => BindingMargin(document.PageCount);
    private static double BindingMargin(int pageNumber) => pageNumber % 2 == 1 ? InnerMargin : OuterMargin;
    private const double Padding = 13, Gap = 10, LineHeight = 14, Radius = 9;
    private const string Blue = "#2458CB", Ink = "#172033", Muted = "#65758B";
    private const string Background = "#FFFFFF", Border = "#DCE4EE", CardBorder = "#B8CFF0";
    private const string Amber = "#EC9B0B", NoteBackground = "#FFFAE8", NoteInk = "#85450C";
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private readonly XFont body = Font(9.5), bold = Font(9.5, true), small = Font(8);
    private readonly XFont tocReference = Font(10, true), heading = Font(16, true);
    private readonly XFont reference = Font(typography.PdfReferenceFontSize, true);
    private readonly XFont verse = Font(typography.PdfVerseTextFontSize), verseBold = Font(typography.PdfVerseTextFontSize, true);
    private readonly XFont observation = Font(typography.PdfObservationFontSize);
    private readonly XFont observationTitle = Font(Math.Max(7, typography.PdfObservationFontSize * 7.5 / 9.5), true);
    // PDFsharp font sizes and all measured distances are already in points.
    private double ReferenceLine => Math.Max(LineHeight, typography.PdfReferenceFontSize * 1.4);
    private double VerseLine => Math.Max(LineHeight, typography.PdfVerseTextFontSize * 1.4);
    private double ObservationLine => Math.Max(LineHeight, typography.PdfObservationFontSize * 1.4);
    private double NoteTitleHeight => Math.Max(14, observationTitle.Size * 1.4);
    private double NoteOverhead => 16 + NoteTitleHeight;
    private XGraphics graphics = null!;
    private double y;
    private readonly List<TocEntry> contents = [];
    private readonly Dictionary<string, ThemeDestination> destinations = [];
    private string? startingThemeBookmark;
    private sealed record TocEntry(string Bookmark, PdfPage Page, double Left, double Top, double Height, double NumberTop);
    private sealed record ThemeDestination(int PageNumber, double Top);

    private static XFont Font(double size, bool bold = false) => new("Open Sans", size,
        bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
    private static XColor Color(string hex) => XColor.FromArgb(int.Parse("FF" + hex[1..], NumberStyles.HexNumber));
    private static XBrush Brush(string hex) => new XSolidBrush(Color(hex));
    private static string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    public void Render(ThemeVerseReport report)
    {
        NewPage();
        MainHeader(report);
        AddTableOfContents(report.Sections);
        for (var index = 0; index < report.Sections.Count; index++)
        {
            var section = report.Sections[index];
            startingThemeBookmark = CreateThemeBookmarkName(section, index);
            ct.ThrowIfCancellationRequested();
            var firstHeight = section.References.Count == 0 ? 32 : CardHeight(section.References[0]);
            var freshCapacity = Bottom - TopMargin - ThemeHeight(section);
            ThemeHeader(section, false, firstHeight <= freshCapacity ? firstHeight : MinimumCardHeight(section.References[0]));
            if (section.References.Count == 0)
            {
                Text("Nenhum versículo vinculado a este tema.", body, Muted, Margin + Padding, y);
                y += 32;
            }
            foreach (var item in section.References) Card(section, item);
        }
        graphics.Dispose();
        graphics = null!;
        CompleteTableOfContents();
        Footer(report.GeneratedAt.ToLocalTime().Year);
    }

    private static string CreateThemeBookmarkName(ThemeVerseReportSection section, int index) =>
        FormattableString.Invariant($"theme-{section.Theme.Id}-section-{index}");

    private void AddTableOfContents(IReadOnlyList<ThemeVerseReportSection> sections)
    {
        if (y + TocHeadingHeight + 60 > Bottom) NewPage();
        TableOfContentsHeading(false);
        for (var index = 0; index < sections.Count; index++)
            AddThemeTableOfContentsEntry(sections[index], index);
        if (sections.Count == 0)
        {
            Text("Nenhum tema incluído no relatório.", body, Muted, Margin, y);
            y += LineHeight;
        }
        y += 20;
    }

    private void TableOfContentsHeading(bool continuation)
    {
        Text(continuation ? "SUMÁRIO • CONTINUAÇÃO" : "SUMÁRIO", heading, Ink, Margin, y);
        y += TocHeadingHeight;
    }

    private void AddThemeTableOfContentsEntry(ThemeVerseReportSection section, int index)
    {
        var lines = Wrap(section.Theme.Name, tocReference, Width - TocNumberWidth - Padding);
        var height = lines.Count * LineHeight + LineHeight + TocEntryGap;
        if (height > Bottom - TopMargin - TocHeadingHeight)
            throw new InvalidOperationException("Nome do tema excede uma página do sumário.");
        if (y + height > Bottom)
        {
            NewPage();
            TableOfContentsHeading(true);
        }
        DrawLines(lines, Margin, y, Blue);
        var numberTop = y + lines.Count * LineHeight;
        var count = Count(section.References.Count, "referência", "referências");
        Text(count, small, Muted, Margin, numberTop);
        var guideStart = Margin + graphics.MeasureString(count, small).Width + Padding;
        var guideEnd = Margin + Width - TocNumberWidth - Padding;
        graphics.DrawLine(new XPen(Color(Muted), .5) { DashStyle = XDashStyle.Dot },
            guideStart, numberTop + 8, guideEnd, numberTop + 8);
        contents.Add(new(CreateThemeBookmarkName(section, index), document.Pages[^1], Margin, y, height, numberTop));
        y += height;
    }

    private void RegisterThemeDestination(ThemeVerseReportSection section)
    {
        var bookmark = startingThemeBookmark ?? throw new InvalidOperationException("Marcador de tema ausente.");
        destinations.Add(bookmark, new(document.PageCount, y));
        var page = document.Pages[^1];
        var pdfTop = page.Height.Point - y;
        document.AddNamedDestination(bookmark, document.PageCount,
            PdfNamedDestinationParameters.CreateVerticalPosition(pdfTop));
        var outline = document.Outlines.Add(section.Theme.Name, page);
        outline.PageDestinationType = PdfPageDestinationType.FitH;
        outline.Top = pdfTop;
    }

    // Page numbers are filled only after the body has been drawn. The reserved number
    // column does not participate in wrapping, so this final pass cannot change pagination.
    private void CompleteTableOfContents()
    {
        foreach (var entry in contents)
        {
            ct.ThrowIfCancellationRequested();
            var destination = destinations[entry.Bookmark];
            using var canvas = XGraphics.FromPdfPage(entry.Page, XGraphicsPdfPageOptions.Append);
            var number = destination.PageNumber.ToString(CultureInfo.InvariantCulture);
            if (canvas.MeasureString(number, bold).Width > TocNumberWidth)
                throw new InvalidOperationException("Número de página excede a coluna do sumário.");
            canvas.DrawString(number, bold, Brush(Blue),
                new XRect(entry.Left + Width - TocNumberWidth, entry.NumberTop, TocNumberWidth, LineHeight), XStringFormats.TopRight);
            // Link rectangles and named destinations use PDF coordinates (origin at bottom left).
            entry.Page.AddDocumentLink(new PdfRectangle(new XRect(entry.Left, entry.Page.Height.Point - entry.Top - entry.Height,
                Width, entry.Height)), entry.Bookmark);
        }
    }

    private void NewPage()
    {
        ct.ThrowIfCancellationRequested();
        graphics?.Dispose();
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(Brush(Background), 0, 0, page.Width.Point, page.Height.Point);
        y = TopMargin;
    }

    private void MainHeader(ThemeVerseReport report)
    {
        var labels = new[] { "VERSÃO DA BÍBLIA", "GERADO EM", "RESUMO DO FILTRO" };
        var values = new[] { report.VersionLabel,
            report.GeneratedAt.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", Portuguese),
            Count(report.Sections.Count, "Tema", "Temas") + " • " + Count(report.Sections.Sum(s => s.References.Count), "Referência", "Referências") };
        var columnWidth = (Width - 36) / 3;
        var lines = values.Select(v => Wrap(v, bold, columnWidth - 12)).ToArray();
        var height = 111 + lines.Max(l => l.Count) * LineHeight;
        Box(Margin, y, Width, height, Blue, Blue);
        Text("BÍBLIATEMA", Font(11, true), "#C7DCFF", Margin + 18, y + 15);
        Box(Margin + Width - 160, y + 14, 142, 18, "#4275DD", "#4275DD");
        Text("RELATÓRIO TEMÁTICO", small, "#FFFFFF", Margin + Width - 152, y + 17);
        graphics.DrawLine(new XPen(Color("#6288DF"), .5), Margin + 18, y + 40, Margin + Width - 18, y + 40);
        Text("Temas e Versículos", Font(23, true), "#FFFFFF", Margin + 18, y + 48);
        for (var i = 0; i < labels.Length; i++)
        {
            var x = Margin + 18 + i * columnWidth;
            Text(labels[i], Font(7.5), "#C7DCFF", x, y + 89);
            DrawLines(lines[i], x, y + 103, "#FFFFFF");
        }
        y += height + 20;
    }

    private double ThemeHeight(ThemeVerseReportSection section) => Wrap(section.Theme.Name, heading, Width - Padding * 2).Count * 22 + 32;

    private double CardHeight(ThemeVerseReportReference item) =>
        2 * Padding + Wrap(item.FormattedReference, reference, Width - 2 * Padding).Count * ReferenceLine + 7
        + Wrap(item.PassageText, verse, Width - 2 * Padding, true).Count * VerseLine
        + (string.IsNullOrWhiteSpace(item.Observation) ? 0 : NoteOverhead + Wrap(item.Observation, observation, Width - 4 * Padding).Count * ObservationLine);

    private double MinimumCardHeight(ThemeVerseReportReference item) =>
        Wrap(item.FormattedReference, reference, Width - 2 * Padding).Count * ReferenceLine + 7
        + 2 * Padding + 3 * Math.Max(VerseLine, ObservationLine);

    private void ThemeHeader(ThemeVerseReportSection section, bool continuation, double reserve = 65)
    {
        var height = ThemeHeight(section);
        if (y + height + reserve > Bottom) NewPage();
        if (!continuation) RegisterThemeDestination(section);
        var accent = SafeAccent(section.Theme.ColorHex);
        graphics.DrawLine(new XPen(Color(accent), 3), Margin, y + 2, Margin, y + height - 14);
        DrawLines(Wrap(section.Theme.Name, heading, Width - Padding * 2), Margin + 10, y, Ink, 22);
        y += height - 28;
        Text(Count(section.References.Count, "referência", "referências") + (continuation ? " • continuação" : ""), small, Muted, Margin + 10, y);
        y += 17;
        graphics.DrawLine(new XPen(Color(Border), 1), Margin, y, Margin + Width, y);
        y += 11;
    }

    private void Card(ThemeVerseReportSection section, ThemeVerseReportReference item)
    {
        var title = Wrap(item.FormattedReference, reference, Width - 2 * Padding);
        var passage = Wrap(item.PassageText, verse, Width - 2 * Padding, true);
        var note = string.IsNullOrWhiteSpace(item.Observation) ? new List<Line>() : Wrap(item.Observation, observation, Width - 4 * Padding);
        var titleHeight = title.Count * ReferenceLine + 7;
        var fullHeight = 2 * Padding + titleHeight + passage.Count * VerseLine + (note.Count > 0 ? NoteOverhead + note.Count * ObservationLine : 0);
        var freshCapacity = Bottom - TopMargin - ThemeHeight(section);
        if (y + fullHeight > Bottom && (fullHeight <= freshCapacity || Bottom - y < titleHeight + 2 * Padding + 3 * Math.Max(VerseLine, ObservationLine)))
        {
            NewPage();
            ThemeHeader(section, true);
        }
        var pi = 0;
        var ni = 0;
        var continued = false;
        do
        {
            ct.ThrowIfCancellationRequested();
            var top = y;
            var available = Bottom - y - 2 * Padding - titleHeight - (continued ? LineHeight : 0);
            var takePassage = Math.Min(passage.Count - pi, Math.Max(0, (int)(available / VerseLine)));
            available -= takePassage * VerseLine;
            var takeNote = pi + takePassage == passage.Count && note.Count > ni
                ? Math.Min(note.Count - ni, Math.Max(0, (int)((available - NoteOverhead) / ObservationLine))) : 0;
            if (takePassage == 0 && takeNote == 0 && (pi < passage.Count || ni < note.Count))
                throw new InvalidOperationException("Cabeçalho da referência excede a área útil do relatório.");
            var height = 2 * Padding + titleHeight + (continued ? LineHeight : 0) + takePassage * VerseLine + (takeNote > 0 ? NoteOverhead + takeNote * ObservationLine : 0);
            Box(Margin, top, Width, height, "#FFFFFF", CardBorder);
            if (note.Count > 0)
                graphics.DrawLine(new XPen(Color(Amber), 3), Margin + 1.5, top + 5, Margin + 1.5, top + height - 5);
            y += Padding;
            DrawLines(title, Margin + Padding, y, Blue, ReferenceLine);
            y += titleHeight;
            if (continued) { Text("Continuação da referência", small, Muted, Margin + Padding, y); y += LineHeight; }
            DrawLines(passage.Skip(pi).Take(takePassage), Margin + Padding, y, Ink, VerseLine);
            y += takePassage * VerseLine;
            if (takeNote > 0)
            {
                y += 8;
                Box(Margin + Padding, y, Width - 2 * Padding, NoteOverhead - 8 + takeNote * ObservationLine, NoteBackground, "#F7E6AD");
                Text("OBSERVAÇÃO:", observationTitle, NoteInk, Margin + 2 * Padding, y + 5);
                DrawLines(note.Skip(ni).Take(takeNote), Margin + 2 * Padding, y + 5 + NoteTitleHeight, NoteInk, ObservationLine);
            }
            pi += takePassage;
            ni += takeNote;
            y = top + height + Gap;
            if (pi < passage.Count || ni < note.Count)
            {
                NewPage();
                ThemeHeader(section, true);
                continued = true;
            }
        } while (pi < passage.Count || ni < note.Count);
    }

    private sealed record Run(string Text, XFont Font);
    private sealed record Line(List<Run> Runs);

    private List<Line> Wrap(string value, XFont font, double width, bool verseNumbers = false)
    {
        var result = new List<Line>();
        foreach (var paragraph in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var runs = new List<Run>();
            double used = 0;
            foreach (Match match in Regex.Matches(paragraph, @"\S+"))
            {
                ct.ThrowIfCancellationRequested();
                var tokenFont = verseNumbers && int.TryParse(match.Value, out _) ? verseBold : font;
                var token = match.Value;
                var prefix = runs.Count == 0 ? "" : " ";
                if (used + graphics.MeasureString(prefix + token, tokenFont).Width > width && runs.Count > 0)
                {
                    result.Add(new(runs)); runs = []; used = 0; prefix = "";
                }
                // Split unusually long tokens by Unicode text element, never by UTF-16 code unit.
                var chunk = prefix;
                var elements = StringInfo.GetTextElementEnumerator(token);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (used + graphics.MeasureString(chunk + element, tokenFont).Width > width && chunk.Length > 0)
                    {
                        runs.Add(new(chunk, tokenFont)); result.Add(new(runs)); runs = []; used = 0; chunk = "";
                    }
                    chunk += element;
                }
                runs.Add(new(chunk, tokenFont));
                used += graphics.MeasureString(chunk, tokenFont).Width;
            }
            result.Add(new(runs));
        }
        return result;
    }

    private void DrawLines(IEnumerable<Line> lines, double x, double top, string color, double spacing = LineHeight)
    {
        foreach (var line in lines)
        {
            var cursor = x;
            foreach (var run in line.Runs)
            {
                Text(run.Text, run.Font, color, cursor, top);
                cursor += graphics.MeasureString(run.Text, run.Font).Width;
            }
            top += spacing;
        }
    }

    private void Text(string text, XFont font, string color, double x, double top) =>
        graphics.DrawString(text, font, Brush(color), new XPoint(x, top), XStringFormats.TopLeft);

    private void Box(double x, double top, double width, double height, string fill, string border) =>
        graphics.DrawRoundedRectangle(new XPen(Color(border), .6), Brush(fill), x, top, width, height, Radius, Radius);

    private static string SafeAccent(string? hex)
    {
        if (hex is null || !Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) return Blue;
        var color = Color(hex);
        // Keep pale user colors visible against the light page without using them for text.
        return color.R * .299 + color.G * .587 + color.B * .114 > 210 ? Blue : hex;
    }

    private void Footer(int year)
    {
        for (var i = 0; i < document.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var footer = XGraphics.FromPdfPage(document.Pages[i], XGraphicsPdfPageOptions.Append);
            var left = BindingMargin(i + 1);
            footer.DrawString($"BíbliaTema • {year}", small, Brush(Muted), new XPoint(left, 817), XStringFormats.TopLeft);
            footer.DrawString($"Página {i + 1} de {document.PageCount}", small, Brush(Muted),
                new XRect(left, 817, Width, 12), XStringFormats.TopRight);
        }
    }

    public void Dispose() => graphics?.Dispose();
}

internal sealed class EmbeddedFontResolver : IFontResolver
{
    private static readonly Lazy<byte[]> Regular = new(() => Load("OpenSans-Regular"));
    private static readonly Lazy<byte[]> Semibold = new(() => Load("OpenSans-Semibold"));
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "OpenSans-Semibold" : "OpenSans-Regular");
    public byte[] GetFont(string faceName) => faceName == "OpenSans-Semibold" ? Semibold.Value : Regular.Value;
    private static byte[] Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Biblia.Fonts.{name}.ttf")
            ?? throw new InvalidOperationException($"Fonte incorporada não encontrada: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
