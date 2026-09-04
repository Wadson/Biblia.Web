using Biblia.Domain.Entities;
namespace Biblia.Application.Interfaces;
public interface IPdfService
{
    Task<string> CreateThemeVersePdfAsync(ThemeVerseReport report, CancellationToken cancellationToken = default);
}
