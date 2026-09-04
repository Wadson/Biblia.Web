using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleVersionManager
{
    Task InitializeCatalogAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BibleVersionCatalogEntry>> GetVersionsAsync(CancellationToken cancellationToken = default);
    Task<BibleVersionCatalogEntry?> GetActiveVersionAsync(CancellationToken cancellationToken = default);
    Task SetActiveVersionAsync(string code, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken = default);
    Task<string> ResolveDatabasePathAsync(string code, CancellationToken cancellationToken = default);
    Task<BibleValidationResult> ValidateAsync(string code, CancellationToken cancellationToken = default);
}
