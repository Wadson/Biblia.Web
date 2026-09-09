using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Biblia.Infrastructure.Files;

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

    public Task<BibleVersionImportResult> ImportAsync(string sourcePath,CancellationToken cancellationToken=default)
        => ImportAsync(sourcePath,null,null,cancellationToken);

    public async Task<BibleVersionImportResult> ImportAsync(string sourcePath,string? requestedCode,string? displayName,CancellationToken cancellationToken=default)
    {
        using var lease=await FileOperationLease.AcquireAsync(paths.AppDataDirectory,cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSource=Path.GetFullPath(sourcePath);
        if(!File.Exists(fullSource))throw new FileNotFoundException("Arquivo selecionado não encontrado.",fullSource);
        if(!new[]{".sqlite",".db"}.Contains(Path.GetExtension(fullSource),StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Selecione um arquivo .sqlite ou .db.");
        if(new FileInfo(fullSource).Length>MaximumDatabaseSize)throw new InvalidDataException("O banco excede o limite de 256 MB.");

        BibleValidationResult Metadata(BibleValidationResult value)
        {
            if(!string.IsNullOrWhiteSpace(displayName)&&value.Issues.Count==1&&value.Issues[0]=="Metadata 'name' ausente.")
                return value with{Status=BibleVersionValidationStatus.Compatible,DisplayName=displayName.Trim(),Issues=[]};
            return value;
        }
        if(displayName is not null && (string.IsNullOrWhiteSpace(displayName)||displayName.Trim().Length>120))throw new ArgumentException("Nome da versão deve conter de 1 a 120 caracteres.");
        var initialValidation=Metadata(await validator.ValidateAsync(fullSource,cancellationToken));
        if(!initialValidation.IsUsable)return new(false,null,initialValidation,initialValidation.Message);
        if(string.IsNullOrWhiteSpace(initialValidation.DisplayName)&&string.IsNullOrWhiteSpace(displayName))throw new ArgumentException("Informe o nome real da versão; o arquivo não possui esse metadado.");
        var code=(requestedCode??initialValidation.Code??"").Trim().ToUpperInvariant();
        if(!Regex.IsMatch(code,"^[A-Z0-9][A-Z0-9_-]{1,19}$"))throw new InvalidDataException("Informe um código de 2 a 20 letras ASCII, números, hífen ou sublinhado.");
        if(await catalog.GetByCodeAsync(code,cancellationToken) is not null)throw new InvalidOperationException($"Já existe uma versão com o código {code}.");

        var directory=Path.Combine(paths.AppDataDirectory,"Bibles","Imported");Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,code+".sqlite");var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        var moved=false;
        try
        {
            await using(var source=new FileStream(fullSource,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan))
            await using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.WriteThrough))
                await source.CopyToAsync(target,cancellationToken);
            var stagedValidation=Metadata(await validator.ValidateAsync(temporary,cancellationToken));
            if(!stagedValidation.IsUsable)return new(false,null,stagedValidation,stagedValidation.Message);
            await using var hashInput=new FileStream(temporary,FileMode.Open,FileAccess.Read,FileShare.Read);
            var sha=Convert.ToHexString(await SHA256.HashDataAsync(hashInput,cancellationToken));
            await hashInput.DisposeAsync();
            File.Move(temporary,destination,false);
            moved=true;
            var now=clock.UtcNow;
            var entry=await catalog.CreateAsync(new BibleVersionCatalogEntry(0,code,displayName?.Trim()??stagedValidation.DisplayName??code,"pt-BR",Path.GetFileName(destination),destination,stagedValidation.SchemaVersion,null,null,null,true,true,false,now,now,stagedValidation.Status,stagedValidation.Message),cancellationToken);
            logger.LogInformation("Versão bíblica importada: {VersionCode}.",code);
            return new(true,entry,stagedValidation,$"Versão {code} importada com sucesso.",sha);
        }
        catch
        {
            if(moved&&File.Exists(destination)&&await catalog.GetByCodeAsync(code,CancellationToken.None) is null)File.Delete(destination);
            throw;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public async Task RemoveImportedAsync(string code,CancellationToken cancellationToken=default)
    {
        using var lease=await FileOperationLease.AcquireAsync(paths.AppDataDirectory,cancellationToken);
        var version=await catalog.GetByCodeAsync(code,cancellationToken)??throw new KeyNotFoundException("Versão não encontrada.");
        if(version.IsBundled)throw new InvalidOperationException("Uma versão empacotada não pode ser removida.");
        var active=await manager.GetActiveVersionAsync(cancellationToken);
        // Delete checks the FK before changing active version or deleting its file.
        try { await catalog.DeleteAsync(version.Id,cancellationToken); }
        catch(Microsoft.Data.Sqlite.SqliteException ex) when(ex.SqliteErrorCode==19)
        { throw new InvalidOperationException("Esta versão é usada por vínculos. Desvincule ou altere os vínculos dependentes antes de remover.",ex); }
        if(string.Equals(active?.Code,version.Code,StringComparison.OrdinalIgnoreCase))
        {
            var manifest=await manifestProvider.GetManifestAsync(cancellationToken);
            await manager.SetActiveVersionAsync(manifest.DefaultVersionCode,cancellationToken);
        }
        if(!string.IsNullOrWhiteSpace(version.InstalledPath)&&File.Exists(version.InstalledPath))File.Delete(version.InstalledPath);
        logger.LogInformation("Versão bíblica importada removida: {VersionCode}.",version.Code);
    }
}
