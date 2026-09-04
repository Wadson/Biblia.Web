using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IThemeService
{
    Task<IReadOnlyList<Theme>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<ThemeDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default);
    Task<Theme> SaveAsync(long? id, string? name, string? colorHex, string? description, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
