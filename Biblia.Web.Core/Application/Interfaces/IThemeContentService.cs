using Biblia.Domain.Entities;
namespace Biblia.Application.Interfaces;
public interface IThemeContentService
{
    Task<ThemeContentSequence> GetAsync(long themeId, CancellationToken ct=default);
    Task SaveBlockAsync(long themeId, long? itemId, ThemeTextBlock block, CancellationToken ct=default);
    Task DeleteBlockAsync(long themeId, long itemId, CancellationToken ct=default);
    Task MoveAsync(long themeId, long itemId, int direction, CancellationToken ct=default);
    Task SetModeAsync(long themeId, ThemeOrderingMode mode, bool confirmCanonical=false, CancellationToken ct=default);
}
