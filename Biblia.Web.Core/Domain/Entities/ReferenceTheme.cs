namespace Biblia.Domain.Entities;

public sealed record ReferenceTheme(
    long ReferenceId,
    long ThemeId,
    string? Observation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long? BibleVersionId = null);
