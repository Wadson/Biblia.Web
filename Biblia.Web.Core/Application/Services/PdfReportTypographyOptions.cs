using System.Text.Json;
using Biblia.Application.Interfaces;

namespace Biblia.Application.Services;

public sealed record PdfReportTypographyOptions(
    double PdfReferenceFontSize = 10,
    double PdfVerseTextFontSize = 9.5,
    double PdfObservationFontSize = 9.5)
{
    public const double ReferenceMin = 8, ReferenceMax = 24;
    public const double VerseMin = 8, VerseMax = 22;
    public const double ObservationMin = 7, ObservationMax = 20;
    public const double Step = .5;
    public static PdfReportTypographyOptions Default { get; } = new();

    public static bool IsValid(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max && value % Step == 0;

    public void Validate()
    {
        Check(PdfReferenceFontSize, ReferenceMin, ReferenceMax, "referência bíblica");
        Check(PdfVerseTextFontSize, VerseMin, VerseMax, "texto do versículo");
        Check(PdfObservationFontSize, ObservationMin, ObservationMax, "observação");
    }

    private static void Check(double value, double min, double max, string label)
    {
        if (!IsValid(value, min, max))
            throw new ArgumentException($"O tamanho de {label} deve estar entre {min} e {max} pt, em incrementos de 0,5 pt.");
    }

    // Invalid stored fields fall back independently; reading never changes existing settings.
    public PdfReportTypographyOptions Normalize() => new(
        IsValid(PdfReferenceFontSize, ReferenceMin, ReferenceMax) ? PdfReferenceFontSize : Default.PdfReferenceFontSize,
        IsValid(PdfVerseTextFontSize, VerseMin, VerseMax) ? PdfVerseTextFontSize : Default.PdfVerseTextFontSize,
        IsValid(PdfObservationFontSize, ObservationMin, ObservationMax) ? PdfObservationFontSize : Default.PdfObservationFontSize);
}

public static class PdfReportSettings
{
    // One existing Setting row makes each save and concurrent report snapshot atomic.
    public const string Key = "PdfReportTypography";

    public static async Task<PdfReportTypographyOptions> GetPdfTypographyAsync(this ISettingsService settings, CancellationToken ct = default)
    {
        var json = await settings.GetAsync(Key, ct);
        if (string.IsNullOrWhiteSpace(json)) return PdfReportTypographyOptions.Default;
        try { return (JsonSerializer.Deserialize<PdfReportTypographyOptions>(json) ?? PdfReportTypographyOptions.Default).Normalize(); }
        catch (JsonException) { return PdfReportTypographyOptions.Default; }
    }

    public static Task SavePdfTypographyAsync(this ISettingsService settings, PdfReportTypographyOptions options, CancellationToken ct = default)
    {
        options.Validate();
        return settings.SetAsync(Key, JsonSerializer.Serialize(options), ct);
    }

    public static Task RestorePdfTypographyAsync(this ISettingsService settings, CancellationToken ct = default) =>
        settings.SavePdfTypographyAsync(PdfReportTypographyOptions.Default, ct);
}
