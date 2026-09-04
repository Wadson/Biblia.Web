using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.ValueObjects;
using Biblia.Infrastructure.BibleDatabases;
using Microsoft.Data.Sqlite;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.BibleDataValidation;

public sealed class BibleRepositoryTests
{
    [Fact]
    public async Task Queries_UseCanonicalReferenceAcrossVersionsAndExposeDuplicates()
    {
        var root=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var first=Path.Combine(root,"ONE.sqlite");var second=Path.Combine(root,"TWO.sqlite");
            await CreateAsync(first,101,"Texto um",false);await CreateAsync(second,9001,"Texto dois",true);
            var repository=new BibleRepository(new VersionManager(new Dictionary<string,string>{{"ONE",first},{"TWO",second}}));
            Assert.Equal(43,(await repository.GetBooksAsync("ONE")).Single().BookReferenceId);
            Assert.Equal(3,(await repository.GetChaptersAsync("ONE",43)).Single());
            Assert.Equal("Texto um",(await repository.GetVerseAsync("ONE",new BibleReference(43,3,16))).Single().Text);
            var candidates=await repository.GetVerseAsync("TWO",new BibleReference(43,3,16));Assert.Equal(2,candidates.Count);Assert.Contains(candidates,x=>x.Text=="Texto dois");
            Assert.True((await repository.GetPassageAsync("TWO",43,3,16,16)).HasAmbiguities);
            Assert.Equal(2,(await repository.SearchAsync("TWO","Texto",43,3)).Count);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task CreateAsync(string path,int id,string text,bool duplicate){await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE book(id INTEGER PRIMARY KEY,book_reference_id INTEGER,testament_reference_id INTEGER,name TEXT);CREATE TABLE verse(id INTEGER PRIMARY KEY,book_id INTEGER,chapter INTEGER,verse INTEGER,text TEXT);INSERT INTO book VALUES(7,43,2,'João');INSERT INTO verse VALUES($id,7,3,16,$text);"+(duplicate?"INSERT INTO verse VALUES($id+1,7,3,16,'Texto duplicado');":"");cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$text",text);await cmd.ExecuteNonQueryAsync();}
    private sealed class VersionManager(IReadOnlyDictionary<string,string> paths):IBibleVersionManager
    {
        public Task<string> ResolveDatabasePathAsync(string code,CancellationToken cancellationToken=default)=>Task.FromResult(paths[code.ToUpperInvariant()]);
        public Task InitializeCatalogAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public Task<IReadOnlyList<BibleVersionCatalogEntry>> GetVersionsAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<BibleVersionCatalogEntry?> GetActiveVersionAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task SetActiveVersionAsync(string code,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task SetEnabledAsync(string code,bool enabled,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<BibleValidationResult> ValidateAsync(string code,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}

#pragma warning restore xUnit1051
