using System.Security.Cryptography;
using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.BibleDatabases;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.Application;

public sealed class BibleVersionImportServiceTests
{
    [Fact]
    public async Task ImportAsync_CopiesValidDatabaseRejectsCollisionsAndPreservesSource()
    {
        var root=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"CST.sqlite");await CreateBibleAsync(source);var originalHash=Hash(source);
            var appRoot=Path.Combine(root,"app");Directory.CreateDirectory(appRoot);var clock=new FixedClock(DateTimeOffset.UtcNow);
            var db=new AppDatabase(Path.Combine(appRoot,"app.db"),NullLogger<AppDatabase>.Instance);var catalog=new BibleVersionCatalogRepository(db);var settings=new SettingsRepository(db,clock);var validator=new BibleValidationService(NullLogger<BibleValidationService>.Instance);var manifest=new ManifestProvider();
            var manager=new BibleVersionManager(catalog,settings,manifest,validator,clock,NullLogger<BibleVersionManager>.Instance);await manager.InitializeCatalogAsync();
            var importer=new BibleVersionImportService(new Paths(appRoot),validator,catalog,manager,manifest,clock,NullLogger<BibleVersionImportService>.Instance);

            var result=await importer.ImportAsync(source);Assert.True(result.Succeeded);Assert.False(result.Version!.IsBundled);Assert.NotEqual(Path.GetFullPath(source),result.Version.InstalledPath);Assert.Equal(originalHash,Hash(source));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>importer.ImportAsync(source));
            await importer.RemoveImportedAsync("CST");Assert.Null(await catalog.GetByCodeAsync("CST"));Assert.True(File.Exists(source));Assert.Equal(originalHash,Hash(source));

            var corrupt=Path.Combine(root,"BAD.sqlite");await File.WriteAllTextAsync(corrupt,"não é sqlite");var rejected=await importer.ImportAsync(corrupt);Assert.False(rejected.Succeeded);Assert.Null(await catalog.GetByCodeAsync("BAD"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Task CreateBibleAsync(string path) => Biblia.Tests.BibleDataValidation.BibleValidationServiceTests.CreateBibleAsync(path);
    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"cache");public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
    private sealed class ManifestProvider:IBibleVersionManifestProvider{public Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new BibleVersionManifest(1,"ACF",[new("ACF","ACF","pt-BR","ACF.sqlite",2,new string('A',64),BibleVersionValidationStatus.Compatible,null)]));}
    private sealed class FixedClock(DateTimeOffset value):IClock{public DateTimeOffset UtcNow{get;}=value;}
}

#pragma warning restore xUnit1051
