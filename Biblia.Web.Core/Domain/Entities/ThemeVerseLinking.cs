namespace Biblia.Domain.Entities;

public sealed record VerseSelection(int BookReferenceId, int Chapter, int VerseStart, int VerseEnd, string? Observation = null);

public sealed record LinkVersesToThemeResult(
    int Selected,
    int ReferencesCreated,
    int ReferencesReused,
    int LinksCreated,
    int AlreadyLinked);

public sealed record ReferenceDisplay(
    SavedReferenceDetails Details,
    string BookName,
    string FormattedReference,
    string? EffectiveVersionCode,
    string? EffectiveVersionName);

public sealed record ThemeVerseLinkDisplay(
    long ReferenceId,
    long ThemeId,
    string BookName,
    int BookReferenceId,
    int Chapter,
    int Verse,
    string FormattedReference,
    string Text,
    string VersionCode,
    string VersionName,
    string? Observation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int VerseEnd);
