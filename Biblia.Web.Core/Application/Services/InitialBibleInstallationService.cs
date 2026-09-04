using System.Security.Cryptography;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class InitialBibleInstallationService(
    IAppPaths paths,
    IPackagedBibleSource packagedSource,
    IBibleVersionManifestProvider manifestProvider,
    IBibleVersionManager versionManager,
    IBibleVersionCatalogRepository catalog,
    IBibleValidationService validator,
    IClock clock,
    ILogger<InitialBibleInstallationService> logger) : IInitialBibleInstallationService
{
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await versionManager.InitializeCatalogAsync(cancellationToken);
        var manifest=await manifestProvider.GetManifestAsync(cancellationToken);
        var directory=Path.Combine(paths.AppDataDirectory,"Bibles");
        Directory.CreateDirectory(directory);

        foreach(var item in manifest.Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination=Path.Combine(directory,item.DatabaseFileName);
            if(File.Exists(destination)&&string.Equals(await ComputeHashAsync(destination,cancellationToken),item.Sha256,StringComparison.OrdinalIgnoreCase))
            {
                await RegisterInstalledAsync(item.Code,destination,item.AuditStatus,item.AuditMessage,cancellationToken);
                continue;
            }

            var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                await using(var source=await packagedSource.OpenReadAsync(item.DatabaseFileName,cancellationToken))
                await using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))
                    await source.CopyToAsync(target,cancellationToken);

                var hash=await ComputeHashAsync(temporary,cancellationToken);
                if(!string.Equals(hash,item.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Hash inválido para {item.Code}.");
                var validation=await validator.ValidateAsync(temporary,cancellationToken);
                if(!validation.IsUsable) throw new InvalidDataException($"Banco {item.Code} incompatível: {validation.Message}");

                File.Move(temporary,destination,true);
                await RegisterInstalledAsync(item.Code,destination,item.AuditStatus,item.AuditMessage,cancellationToken);
                logger.LogInformation("Versão bíblica {VersionCode} instalada.",item.Code);
            }
            finally
            {
                if(File.Exists(temporary)) File.Delete(temporary);
            }
        }

        await versionManager.SetActiveVersionAsync(manifest.DefaultVersionCode,cancellationToken);
    }

    private async Task RegisterInstalledAsync(string code,string path,BibleVersionValidationStatus status,string? message,CancellationToken token)
    {
        var version=await catalog.GetByCodeAsync(code,token)??throw new InvalidOperationException($"Catálogo não contém {code}.");
        await catalog.UpdateAsync(version with{InstalledPath=Path.GetFullPath(path),IsInstalled=true,IsEnabled=true,InstalledAt=version.InstalledAt??clock.UtcNow,UpdatedAt=clock.UtcNow,ValidationStatus=status,ValidationMessage=message},token);
    }

    private static async Task<string> ComputeHashAsync(string path,CancellationToken token)
    {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
    }
}
