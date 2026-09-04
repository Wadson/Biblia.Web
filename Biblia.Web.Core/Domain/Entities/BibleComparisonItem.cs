using Biblia.Domain.Enums;

namespace Biblia.Domain.Entities;

public sealed record BibleComparisonItem(string VersionCode,BibleComparisonStatus Status,IReadOnlyList<BibleVerse> Verses,string? Message);
