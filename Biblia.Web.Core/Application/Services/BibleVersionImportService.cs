using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class BibleVersionImportService(
    IAppPaths paths,
    IBibleValidationService validator,
    IBibleVersionCatalogRepository catalog,
    IBibleVersionManager manager,
    IBibleVersionManifestProvider manifestProvider,
    IClock clock,
    ILogger<BibleVersionImportService> logger):IBibleVersionImportService
{
    private const long MaximumDatabaseSize=256L*1024*1024;

    public async Task<BibleVersionImportResult> ImportAsync(string sourcePath,CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSource=Path.GetFullPath(sourcePath);
        if(!File.Exists(fullSource))throw new FileNotFoundException("Arquivo selecionado não encontrado.",fullSource);
        if(!new[]{".sqlite",".db"}.Contains(Path.GetExtension(fullSource),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Selecione um arquivo .sqlite ou .db.");
        if(new FileInfo(fullSource).Length>MaximumDatabaseSize)throw new InvalidDataException("O banco excede o limite de 256 MB.");

        var initialValidation=await validator.ValidateAsync(fullSource,cancellationToken);
        if(!initialValidation.IsUsable)return new(false,null,initialValidation,initialValidation.Message);
        var code=(initialValidation.Code??Path.GetFileNameWithoutExtension(fullSource)).Trim().ToUpperInvariant();
        if(code.Length is <2 or >20||code.Any(ch=>!char.IsLetterOrDigit(ch)&&ch!='-'&&ch!='_'))throw new InvalidDataException("Não foi possível determinar um código seguro para a versão.");
        if(await catalog.GetByCodeAsync(code,cancellationToken) is not null)throw new InvalidOperationException($"Já existe uma versão com o código {code}.");

        var directory=Path.Combine(paths.AppDataDirectory,"Bibles","Imported");Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,code+".sqlite");var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using(var source=new FileStream(fullSource,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan))
            await using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))
                await source.CopyToAsync(target,cancellationToken);
            var stagedValidation=await validator.ValidateAsync(temporary,cancellationToken);
            if(!stagedValidation.IsUsable)return new(false,null,stagedValidation,stagedValidation.Message);
            File.Move(temporary,destination,false);
            var now=clock.UtcNow;
            var entry=await catalog.CreateAsync(new BibleVersionCatalogEntry(0,code,stagedValidation.DisplayName??code,"pt-BR",Path.GetFileName(destination),destination,stagedValidation.SchemaVersion,null,null,null,true,true,false,now,now,stagedValidation.Status,stagedValidation.Message),cancellationToken);
            logger.LogInformation("Versão bíblica importada: {VersionCode}.",code);
            return new(true,entry,stagedValidation,$"Versão {code} importada com sucesso.");
        }
        catch
        {
            if(File.Exists(destination)&&await catalog.GetByCodeAsync(code,cancellationToken) is null)File.Delete(destination);
            throw;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public async Task RemoveImportedAsync(string code,CancellationToken cancellationToken=default)
    {
        var version=await catalog.GetByCodeAsync(code,cancellationToken)??throw new KeyNotFoundException("Versão não encontrada.");
        if(version.IsBundled)throw new InvalidOperationException("Uma versão empacotada não pode ser removida.");
        var active=await manager.GetActiveVersionAsync(cancellationToken);
        if(string.Equals(active?.Code,version.Code,StringComparison.OrdinalIgnoreCase))
        {
            var manifest=await manifestProvider.GetManifestAsync(cancellationToken);
            await manager.SetActiveVersionAsync(manifest.DefaultVersionCode,cancellationToken);
        }
        await catalog.DeleteAsync(version.Id,cancellationToken);
        if(!string.IsNullOrWhiteSpace(version.InstalledPath)&&File.Exists(version.InstalledPath))File.Delete(version.InstalledPath);
        logger.LogInformation("Versão bíblica importada removida: {VersionCode}.",version.Code);
    }
}
