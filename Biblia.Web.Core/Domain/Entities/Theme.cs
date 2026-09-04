namespace Biblia.Domain.Entities;

public sealed record Theme(long Id, string Name, string? ColorHex, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
