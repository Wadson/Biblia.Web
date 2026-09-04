using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleSearchService
{
    Task<BibleSearchResult> SearchAsync(BibleSearchQuery query,CancellationToken cancellationToken=default);
}
