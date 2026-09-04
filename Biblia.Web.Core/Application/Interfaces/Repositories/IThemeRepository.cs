using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces.Repositories;

public interface IThemeRepository
{
    Task<Theme> CreateAsync(string name, string? colorHex, string? description, CancellationToken cancellationToken = default);
    Task<Theme?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Theme>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Theme>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<ThemeUsage> GetUsageAsync(long id, CancellationToken cancellationToken = default);
    Task UpdateAsync(Theme theme, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public sealed record ThemeUsage(int ReferencesCount);
