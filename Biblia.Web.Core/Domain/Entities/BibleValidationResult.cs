using Biblia.Domain.Enums;

namespace Biblia.Domain.Entities;

public sealed record BibleValidationResult(
    BibleVersionValidationStatus Status,
    string? Code,
    string? DisplayName,
    int SchemaVersion,
    int BookCount,
    int VerseCount,
    int DuplicateReferenceCount,
    IReadOnlyList<string> Issues)
{
    public bool IsUsable => Status is BibleVersionValidationStatus.Compatible or BibleVersionValidationStatus.CompatibleWithCaveat;
    public string Message => Issues.Count == 0 ? "Banco bíblico compatível." : string.Join(" ", Issues);
}
