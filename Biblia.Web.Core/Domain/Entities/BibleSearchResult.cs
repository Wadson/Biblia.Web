namespace Biblia.Domain.Entities;

public sealed record BibleSearchResult(IReadOnlyList<BibleVerse> Items,int TotalReturned,TimeSpan Elapsed);
