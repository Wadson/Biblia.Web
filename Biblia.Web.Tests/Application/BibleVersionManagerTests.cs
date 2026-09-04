using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.Application;

public sealed class BibleVersionManagerTests
{
    [Fact]
    public async Task InitializeAndActivate_AreIdempotentAndRequireUsableInstallation()
    {
        var directory=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var db=new AppDatabase(Path.Combine(directory,"app.db"),NullLogger<AppDatabase>.Instance);
            var clock=new FixedClock(new DateTimeOffset(2026,8,18,12,0,0,TimeSpan.Zero));
            var catalog=new BibleVersionCatalogRepository(db);var settings=new SettingsRepository(db,clock);
            var manager=new BibleVersionManager(catalog,settings,new ManifestProvider(),new Validator(),clock,NullLogger<BibleVersionManager>.Instance);
            await manager.InitializeCatalogAsync();await manager.InitializeCatalogAsync();
            Assert.Single(await manager.GetVersionsAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.SetActiveVersionAsync("ACF"));

            var file=Path.Combine(directory,"ACF.sqlite");await File.WriteAllBytesAsync(file,[1]);
            var version=(await catalog.GetByCodeAsync("ACF"))!;
            await catalog.UpdateAsync(version with{InstalledPath=file,IsInstalled=true,IsEnabled=true,ValidationStatus=BibleVersionValidationStatus.Compatible});
            await manager.SetActiveVersionAsync("acf");
            Assert.Equal("ACF",(await manager.GetActiveVersionAsync())!.Code);
            Assert.Equal(Path.GetFullPath(file),await manager.ResolveDatabasePathAsync("ACF"));
            Assert.True((await manager.ValidateAsync("ACF")).IsUsable);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    private sealed class ManifestProvider:IBibleVersionManifestProvider{public Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new BibleVersionManifest(1,"ACF",[new("ACF","Almeida Corrigida e Fiel","pt-BR","ACF.sqlite",2,new string('A',64),BibleVersionValidationStatus.Compatible,null)]));}
    private sealed class Validator:IBibleValidationService{public Task<BibleValidationResult> ValidateAsync(string databasePath,CancellationToken cancellationToken=default)=>Task.FromResult(new BibleValidationResult(BibleVersionValidationStatus.Compatible,"ACF","Almeida Corrigida e Fiel",2,66,31102,0,[]));}
    private sealed class FixedClock(DateTimeOffset value):IClock{public DateTimeOffset UtcNow{get;}=value;}
}

#pragma warning restore xUnit1051
