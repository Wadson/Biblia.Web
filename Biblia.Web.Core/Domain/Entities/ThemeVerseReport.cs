namespace Biblia.Domain.Entities;

public sealed record ThemeVerseReportRequest(long? ThemeId,long? PublicationId = null,string? TitleOverride = null);

public sealed record ThemeVerseReport(
    string Title,
    DateTimeOffset GeneratedAt,
    int ThemeCount,
    int ReferenceCount,
    IReadOnlyList<ThemeVerseReportSection> Sections,
    PdfHeaderOptions? Header = null);

public sealed record PdfHeaderOptions(
    string? OrganizationName, string? Subtitle, string? HeaderText,
    string BackgroundColorHex, string TitleTextColorHex, string OrganizationTextColorHex,
    string SubtitleTextColorHex, string HeaderDetailTextColorHex,
    double TitleFontSize, double OrganizationFontSize, double SubtitleFontSize, double HeaderDetailFontSize,
    bool TitleBold, bool TitleItalic, byte[]? Logo, string? LogoContentType,
    double? LogoWidth, double? LogoHeight);

public sealed record ThemeVerseReportSection(Theme Theme, IReadOnlyList<ThemeVerseReportReference> References,
    IReadOnlyList<ThemeReportContentItem>? Content = null, ThemeOrderingMode OrderingMode = ThemeOrderingMode.Canonical);

public sealed record ThemeVerseReportReference(
    long SavedReferenceId,
    string FormattedReference,
    string PassageText,
    string? Observation,
    int BookReferenceId,
    int Chapter,
    int VerseStart,
    int VerseEnd,
    string VersionCode = "");
