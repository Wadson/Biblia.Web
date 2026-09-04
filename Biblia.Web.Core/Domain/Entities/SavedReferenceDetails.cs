namespace Biblia.Domain.Entities;

public sealed record SavedReferenceDetails(SavedReference Reference, IReadOnlyList<Theme> Themes);
