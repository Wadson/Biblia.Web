using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces.Repositories;

public interface ISettingsRepository
{
    Task<Setting?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
