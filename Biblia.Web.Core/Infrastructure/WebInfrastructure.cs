using System.Text.Json;
using System.Text.Json.Serialization;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Infrastructure.BibleDatabases;
using Biblia.Infrastructure.Files;
using Biblia.Infrastructure.Repositories;
using Biblia.Infrastructure.Time;
using Biblia.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Biblia.Infrastructure;

public sealed class LocalAppPaths : IAppPaths
{
    public LocalAppPaths()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var isolated=Environment.GetEnvironmentVariable("BIBLIATEMA_DATA_DIR");
        if(!string.IsNullOrWhiteSpace(isolated)&&!Path.IsPathFullyQualified(isolated))throw new ArgumentException("BIBLIATEMA_DATA_DIR deve ser absoluto.");
        AppDataDirectory = string.IsNullOrWhiteSpace(isolated)?Path.Combine(root, "BibliaTema"):Path.GetFullPath(isolated);
        CacheDirectory = Path.Combine(AppDataDirectory, "Cache");
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
    public string AppDataDirectory { get; }
    public string CacheDirectory { get; }
    public string GetPrivateFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            throw new ArgumentException("Nome de arquivo inválido.", nameof(fileName));
        return Path.Combine(AppDataDirectory, fileName);
    }
}

public sealed class WebPackagedBibleSource : IPackagedBibleSource
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Content", "Bibles");
    public Task<Stream> OpenReadAsync(string databaseFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseFileName) || databaseFileName != Path.GetFileName(databaseFileName))
            throw new ArgumentException("Nome de banco inválido.", nameof(databaseFileName));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(Path.Combine(Root, databaseFileName), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }
}

public sealed class WebBibleVersionManifestProvider : IBibleVersionManifestProvider
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public async Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "Bibles", "bible-versions.manifest.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BibleVersionManifest>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("Manifesto de versões inválido.");
    }
}

public static class WebInfrastructureRegistration
{
    public static IServiceCollection AddWebInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, LocalAppPaths>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<AppDatabase.AppDatabase>();
        services.AddSingleton<IAppDatabase>(p => p.GetRequiredService<AppDatabase.AppDatabase>());
        services.AddSingleton<IThemeRepository, ThemeRepository>();
        services.AddSingleton<IThemeContentService, ThemeContentService>();
        services.AddSingleton<ISavedReferenceRepository, SavedReferenceRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IBibleVersionCatalogRepository, BibleVersionCatalogRepository>();
        services.AddSingleton<IBibleValidationService, BibleValidationService>();
        services.AddSingleton<IBibleRepository, BibleRepository>();
        services.AddSingleton<IBibleVersionManifestProvider, WebBibleVersionManifestProvider>();
        services.AddSingleton<IPackagedBibleSource, WebPackagedBibleSource>();
        services.AddSingleton<IPdfService, PdfService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<INatureMediaService, OfflineNatureMediaService>();
        services.AddSingleton<IVerseCardService, SkiaVerseCardService>();
        return services;
    }
}

public sealed class OfflineNatureMediaService : INatureMediaService
{
    private static readonly IReadOnlyList<NaturePhoto> Backgrounds =
    [
        new(-1,"","","BíbliaTema","","","#0D3B66",true,"#2A9D8F"),
        new(-2,"","","BíbliaTema","","","#264653",true,"#E9C46A"),
        new(-3,"","","BíbliaTema","","","#6D597A",true,"#E56B6F")
    ];
    public IReadOnlyList<NaturePhoto> GetOfflineBackgrounds()=>Backgrounds;
    public Task<IReadOnlyList<NaturePhoto>> SearchAsync(string query,int page=1,int pageSize=8,CancellationToken cancellationToken=default)=>Task.FromResult(Backgrounds);
    public Task<string?> GetRenderFileAsync(NaturePhoto photo,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(null);
}
