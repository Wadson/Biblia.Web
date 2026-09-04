using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleVersionManifestProvider
{
    Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken = default);
}
