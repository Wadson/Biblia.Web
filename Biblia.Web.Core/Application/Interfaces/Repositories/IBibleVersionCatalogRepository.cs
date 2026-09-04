using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces.Repositories;

public interface IBibleVersionCatalogRepository
{
    Task<BibleVersionCatalogEntry> CreateAsync(BibleVersionCatalogEntry entry, CancellationToken cancellationToken = default);
    Task<BibleVersionCatalogEntry?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BibleVersionCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(BibleVersionCatalogEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
