using Biblia.Domain.Enums;

namespace Biblia.Domain.Entities;

public sealed record BibleVersionCatalogEntry(
    long Id,
    string Code,
    string DisplayName,
    string Language,
    string DatabaseFileName,
    string? InstalledPath,
    int SchemaVersion,
    string? LicenseName,
    string? LicenseText,
    string? Attribution,
    bool IsInstalled,
    bool IsEnabled,
    bool IsBundled,
    DateTimeOffset? InstalledAt,
    DateTimeOffset? UpdatedAt,
    BibleVersionValidationStatus ValidationStatus,
    string? ValidationMessage);
