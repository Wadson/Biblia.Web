using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IReportService
{
    Task<ReportsOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<ThemeVerseReport> BuildThemesAsync(ThemeVerseReportRequest request, CancellationToken cancellationToken = default);
}
