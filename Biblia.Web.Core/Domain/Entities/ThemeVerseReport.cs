namespace Biblia.Domain.Entities;

public sealed record ThemeVerseReportRequest(long? ThemeId, string BibleVersionCode);

public sealed record ThemeVerseReport(
    string Title,
    string VersionLabel,
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
    int VerseEnd);
