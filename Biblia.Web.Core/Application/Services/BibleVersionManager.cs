using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class BibleVersionManager(
    IBibleVersionCatalogRepository catalog,
    ISettingsRepository settings,
    IBibleVersionManifestProvider manifestProvider,
    IBibleValidationService validator,
    IClock clock,
    ILogger<BibleVersionManager> logger) : IBibleVersionManager
{
    private const string ActiveVersionKey = "Bible.ActiveVersionCode";

    public async Task InitializeCatalogAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await manifestProvider.GetManifestAsync(cancellationToken);
        foreach (var item in manifest.Versions)
        {
            var existing = await catalog.GetByCodeAsync(item.Code, cancellationToken);
            if (existing is null)
            {
                await catalog.CreateAsync(new BibleVersionCatalogEntry(0,item.Code,item.DisplayName,item.Language,item.DatabaseFileName,null,item.SchemaVersion,null,null,null,false,false,true,null,clock.UtcNow,item.AuditStatus,item.AuditMessage),cancellationToken);
            }
            else
            {
                await catalog.UpdateAsync(existing with { DisplayName=item.DisplayName,Language=item.Language,DatabaseFileName=item.DatabaseFileName,SchemaVersion=item.SchemaVersion,IsBundled=true,ValidationStatus=item.AuditStatus,ValidationMessage=item.AuditMessage,UpdatedAt=clock.UtcNow },cancellationToken);
            }
        }

        if (await settings.GetAsync(ActiveVersionKey, cancellationToken) is null)
            await settings.SetAsync(ActiveVersionKey, manifest.DefaultVersionCode, cancellationToken);
    }

    public Task<IReadOnlyList<BibleVersionCatalogEntry>> GetVersionsAsync(CancellationToken cancellationToken=default)=>catalog.GetAllAsync(cancellationToken);

    public async Task<BibleVersionCatalogEntry?> GetActiveVersionAsync(CancellationToken cancellationToken=default)
    {
        var setting=await settings.GetAsync(ActiveVersionKey,cancellationToken);
        return setting is null?null:await catalog.GetByCodeAsync(setting.Value,cancellationToken);
    }

    public async Task SetActiveVersionAsync(string code,CancellationToken cancellationToken=default)
    {
        var version=await RequireVersionAsync(code,cancellationToken);
        if(!version.IsInstalled||!version.IsEnabled||version.ValidationStatus==BibleVersionValidationStatus.Incompatible)
            throw new InvalidOperationException("Somente uma versão instalada, habilitada e compatível pode ser ativada.");
        await settings.SetAsync(ActiveVersionKey,version.Code,cancellationToken);
        logger.LogInformation("Versão bíblica ativa alterada para {VersionCode}.",version.Code);
    }

    public async Task SetEnabledAsync(string code,bool enabled,CancellationToken cancellationToken=default)
    {
        var version=await RequireVersionAsync(code,cancellationToken);
        if(enabled&&(!version.IsInstalled||version.ValidationStatus==BibleVersionValidationStatus.Incompatible))
            throw new InvalidOperationException("Uma versão não instalada ou incompatível não pode ser habilitada.");
        await catalog.UpdateAsync(version with{IsEnabled=enabled,UpdatedAt=clock.UtcNow},cancellationToken);
    }

    public async Task<string> ResolveDatabasePathAsync(string code,CancellationToken cancellationToken=default)
    {
        var version=await RequireVersionAsync(code,cancellationToken);
        if(!version.IsInstalled||string.IsNullOrWhiteSpace(version.InstalledPath)||!File.Exists(version.InstalledPath))
            throw new FileNotFoundException("O banco da versão não está instalado.",version.InstalledPath);
        return Path.GetFullPath(version.InstalledPath);
    }

    public async Task<BibleValidationResult> ValidateAsync(string code,CancellationToken cancellationToken=default)
    {
        var version=await RequireVersionAsync(code,cancellationToken);
        if(string.IsNullOrWhiteSpace(version.InstalledPath)) throw new InvalidOperationException("A versão ainda não possui arquivo instalado.");
        var result=await validator.ValidateAsync(version.InstalledPath,cancellationToken);
        await catalog.UpdateAsync(version with{ValidationStatus=result.Status,ValidationMessage=result.Message,UpdatedAt=clock.UtcNow},cancellationToken);
        return result;
    }

    private async Task<BibleVersionCatalogEntry> RequireVersionAsync(string code,CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return await catalog.GetByCodeAsync(code,token)??throw new KeyNotFoundException($"Versão bíblica não encontrada: {code}.");
    }
}
