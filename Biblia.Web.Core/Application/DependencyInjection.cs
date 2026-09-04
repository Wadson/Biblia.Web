using Microsoft.Extensions.DependencyInjection;
using Biblia.Application.Interfaces;
using Biblia.Application.Services;

namespace Biblia.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBibleVersionManager, BibleVersionManager>();
        services.AddSingleton<IInitialBibleInstallationService, InitialBibleInstallationService>();
        services.AddSingleton<IAppInitializationService, AppInitializationService>();
        services.AddSingleton<IBibleVersionImportService, BibleVersionImportService>();
        services.AddSingleton<IBibleSearchService, BibleSearchService>();
        services.AddSingleton<IBibleComparisonService, BibleComparisonService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISavedReferenceService, SavedReferenceService>();
        services.AddSingleton<IBookNameResolver, BookNameResolver>();
        services.AddSingleton<IThemeVerseLinkService, ThemeVerseLinkService>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDailyVerseService, DailyVerseService>();
        return services;
    }
}
