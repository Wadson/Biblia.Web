using System.IO.Compression;
using System.Text.Json;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Files;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biblia.Tests.Infrastructure;

#pragma warning disable xUnit1051
public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateAndValidate_ProduceCompleteUsableZip()
    {
        await using var context=await Context.CreateAsync();
        await context.Themes.CreateAsync("Esperança","#173A63",null);
        var backup=await context.Service.CreateAsync();

        Assert.True(File.Exists(backup.Path));
        using var archive=ZipFile.OpenRead(backup.Path);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("bibliatema.db"));
        var validated=await context.Service.ValidateAsync(backup.Path);
        Assert.Equal(AppDatabase.CurrentSchemaVersion,validated.SchemaVersion);
    }

    [Theory]
    [InlineData(false,true,"Backup sem manifesto.")]
    [InlineData(true,false,"Backup sem banco do usuário.")]
    public async Task Validate_RejectsMissingRequiredEntry(bool manifest,bool database,string message)
    {
        await using var context=await Context.CreateAsync();
        var path=Path.Combine(context.Root,"invalid.zip");
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            if(manifest)await WriteAsync(archive,"manifest.json",JsonSerializer.Serialize(new{CreatedAt=DateTimeOffset.UtcNow,SchemaVersion=AppDatabase.CurrentSchemaVersion}));
            if(database)await WriteAsync(archive,"bibliatema.db","invalid");
        }
        var exception=await Assert.ThrowsAsync<InvalidDataException>(()=>context.Service.ValidateAsync(path));
        Assert.Equal(message,exception.Message);
    }

    [Fact]
    public async Task Validate_RejectsNewerSchema()
    {
        await using var context=await Context.CreateAsync();
        var path=Path.Combine(context.Root,"future.zip");
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            await WriteAsync(archive,"manifest.json",JsonSerializer.Serialize(new{CreatedAt=DateTimeOffset.UtcNow,SchemaVersion=AppDatabase.CurrentSchemaVersion+1}));
            await WriteAsync(archive,"bibliatema.db","placeholder");
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>context.Service.ValidateAsync(path));
    }

    [Fact]
    public async Task Restore_ReplacesDataAndDatabaseRemainsOperational()
    {
        await using var context=await Context.CreateAsync();
        await context.Themes.CreateAsync("Antes","#173A63",null);
        var backup=await context.Service.CreateAsync();
        await context.Themes.CreateAsync("Depois","#2563EB",null);

        await context.Service.RestoreAsync(backup.Path);

        Assert.Equal(["Antes"],(await context.Themes.GetAllAsync()).Select(x=>x.Name));
        var created=await context.Themes.CreateAsync("Após restauração","#059669",null);
        Assert.True(created.Id>0);
        Assert.Equal(2,(await context.Themes.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Restore_InvalidDatabase_PreservesActiveDatabaseAndCreatesSafetyBackup()
    {
        await using var context=await Context.CreateAsync();
        await context.Themes.CreateAsync("Preservado","#173A63",null);
        var invalid=Path.Combine(context.Root,"corrupt.zip");
        using(var archive=ZipFile.Open(invalid,ZipArchiveMode.Create))
        {
            await WriteAsync(archive,"manifest.json",JsonSerializer.Serialize(new{CreatedAt=DateTimeOffset.UtcNow,SchemaVersion=AppDatabase.CurrentSchemaVersion}));
            await WriteAsync(archive,"bibliatema.db","not sqlite");
        }

        await Assert.ThrowsAnyAsync<Exception>(()=>context.Service.RestoreAsync(invalid));

        Assert.Equal("Preservado",(await context.Themes.GetAllAsync()).Single().Name);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(context.Root,"backups"),"bibliatema-*.zip"));
    }

    private static async Task WriteAsync(ZipArchive archive,string name,string content)
    {
        await using var stream=archive.CreateEntry(name).Open();
        await using var writer=new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private sealed class Context : IAsyncDisposable
    {
        private Context(string root,AppDatabase database,ThemeRepository themes,BackupService service){Root=root;Database=database;Themes=themes;Service=service;}
        public string Root{get;} public AppDatabase Database{get;} public ThemeRepository Themes{get;} public BackupService Service{get;}
        public static async Task<Context> CreateAsync()
        {
            var root=Path.Combine(Path.GetTempPath(),"BibliaTema.BackupTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var database=new AppDatabase(Path.Combine(root,"bibliatema.db"),NullLogger<AppDatabase>.Instance);await database.InitializeAsync();
            var themes=new ThemeRepository(database,new Clock());
            return new(root,database,themes,new BackupService(database,new Paths(root),NullLogger<BackupService>.Instance));
        }
        public ValueTask DisposeAsync(){SqliteConnection.ClearAllPools();if(Directory.Exists(Root))Directory.Delete(Root,true);return ValueTask.CompletedTask;}
    }
    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>root;public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
    private sealed class Clock:IClock{public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow;}
}
#pragma warning restore xUnit1051
