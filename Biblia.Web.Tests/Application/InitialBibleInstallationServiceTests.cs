using System.Security.Cryptography;
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

public sealed class InitialBibleInstallationServiceTests
{
    [Fact]
    public async Task InstallAsync_CopiesValidPackageOnceAndRegistersDefaultVersion()
    {
        var directory=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var bytes="banco bíblico empacotado"u8.ToArray();var hash=Convert.ToHexString(SHA256.HashData(bytes));
            var manifest=new BibleVersionManifest(1,"ACF",[new("ACF","Almeida Corrigida e Fiel","pt-BR","ACF.sqlite",2,hash,BibleVersionValidationStatus.Compatible,null)]);
            var provider=new ManifestProvider(manifest);var source=new PackageSource(bytes);var clock=new FixedClock(DateTimeOffset.UtcNow);
            var db=new AppDatabase(Path.Combine(directory,"app.db"),NullLogger<AppDatabase>.Instance);var catalog=new BibleVersionCatalogRepository(db);var settings=new SettingsRepository(db,clock);var validator=new Validator();
            var manager=new BibleVersionManager(catalog,settings,provider,validator,clock,NullLogger<BibleVersionManager>.Instance);
            var installer=new InitialBibleInstallationService(new Paths(directory),source,provider,manager,catalog,validator,clock,NullLogger<InitialBibleInstallationService>.Instance);

            await installer.InstallAsync();await installer.InstallAsync();

            Assert.Equal(1,source.OpenCount);
            Assert.Equal(bytes,await File.ReadAllBytesAsync(Path.Combine(directory,"Bibles","ACF.sqlite")));
            var version=(await catalog.GetByCodeAsync("ACF"))!;Assert.True(version.IsInstalled);Assert.True(version.IsEnabled);
            Assert.Equal("ACF",(await manager.GetActiveVersionAsync())!.Code);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"cache");public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
    private sealed class ManifestProvider(BibleVersionManifest value):IBibleVersionManifestProvider{public Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken=default)=>Task.FromResult(value);}
    private sealed class PackageSource(byte[] bytes):IPackagedBibleSource{public int OpenCount{get;private set;}public Task<Stream> OpenReadAsync(string databaseFileName,CancellationToken cancellationToken=default){OpenCount++;return Task.FromResult<Stream>(new MemoryStream(bytes,false));}}
    private sealed class Validator:IBibleValidationService{public Task<BibleValidationResult> ValidateAsync(string databasePath,CancellationToken cancellationToken=default)=>Task.FromResult(new BibleValidationResult(BibleVersionValidationStatus.Compatible,"ACF","ACF",2,66,31102,0,[]));}
    private sealed class FixedClock(DateTimeOffset value):IClock{public DateTimeOffset UtcNow{get;}=value;}
}

#pragma warning restore xUnit1051
