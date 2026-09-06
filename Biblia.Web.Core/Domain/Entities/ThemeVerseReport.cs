namespace Biblia.Domain.Entities;

public sealed record ThemeVerseReportRequest(long? ThemeId);

public sealed record ThemeVerseReport(
    string Title,
    DateTimeOffset GeneratedAt,
    int ThemeCount,
    int ReferenceCount,
    IReadOnlyList<ThemeVerseReportSection> Sections);

public sealed record ThemeVerseReportSection(Theme Theme, IReadOnlyList<ThemeVerseReportReference> References);

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
