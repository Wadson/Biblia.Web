namespace Biblia.Domain.Entities;

public sealed record SavedReference(long Id, int BookReferenceId, int Chapter, int VerseStart, int VerseEnd, string? Comment, long? PreferredBibleVersionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
