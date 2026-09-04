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
    private static async Task CreateBibleAsync(string path){await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="""CREATE TABLE book(id INTEGER PRIMARY KEY,book_reference_id INTEGER,testament_reference_id INTEGER,name TEXT);CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT);CREATE TABLE verse(id INTEGER PRIMARY KEY,book_id INTEGER,chapter INTEGER,verse INTEGER,text TEXT);INSERT INTO metadata VALUES('dbversion','2'),('name','Custom');WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<66) INSERT INTO book SELECT x,x,CASE WHEN x<40 THEN 1 ELSE 2 END,'Livro'||x FROM n;WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<30000) INSERT INTO verse SELECT x,((x-1)%66)+1,((x-1)/1000)+1,((x-1)%1000)+1,'Texto'||x FROM n;""";await cmd.ExecuteNonQueryAsync();}
    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"cache");public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
    private sealed class ManifestProvider:IBibleVersionManifestProvider{public Task<BibleVersionManifest> GetManifestAsync(CancellationToken cancellationToken=default)=>Task.FromResult(new BibleVersionManifest(1,"ACF",[new("ACF","ACF","pt-BR","ACF.sqlite",2,new string('A',64),BibleVersionValidationStatus.Compatible,null)]));}
    private sealed class FixedClock(DateTimeOffset value):IClock{public DateTimeOffset UtcNow{get;}=value;}
}

#pragma warning restore xUnit1051
