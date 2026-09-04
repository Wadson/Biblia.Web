namespace Biblia.Domain.Entities;

public sealed record BibleVersionImportResult(bool Succeeded, BibleVersionCatalogEntry? Version, BibleValidationResult Validation, string Message);
