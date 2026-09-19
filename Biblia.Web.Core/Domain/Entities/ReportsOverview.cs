namespace Biblia.Domain.Entities;

public sealed record ReportsOverview(
    int Themes,
    int References,
    int ReferencesWithComments,
    int ThemeVerseLinks,
    int InstalledBibleVersions);
