using Biblia.Domain.Enums;

namespace Biblia.Domain.Entities;

public sealed record BibleVersionManifest(int ManifestVersion, string DefaultVersionCode, IReadOnlyList<BibleVersionManifestEntry> Versions);

public sealed record BibleVersionManifestEntry(
    string Code,
    string DisplayName,
    string Language,
    string DatabaseFileName,
    int SchemaVersion,
    string Sha256,
    BibleVersionValidationStatus AuditStatus,
    string? AuditMessage);
