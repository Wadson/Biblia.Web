namespace Biblia.Domain.Entities;

public sealed record Setting(string Key, string Value, DateTimeOffset UpdatedAt);
