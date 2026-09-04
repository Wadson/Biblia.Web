using Biblia.Domain.Enums;
using Biblia.Infrastructure.BibleDatabases;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.BibleDataValidation;

public sealed class BibleValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_ClassifiesValidAndDuplicateDatabases()
    {
        var directory=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var valid=Path.Combine(directory,"TST.sqlite");
        try
        {
            await CreateBibleAsync(valid);
            var service=new BibleValidationService(NullLogger<BibleValidationService>.Instance);
            var result=await service.ValidateAsync(valid);
            Assert.Equal(BibleVersionValidationStatus.Compatible,result.Status);
            Assert.Equal(66,result.BookCount);
            Assert.Equal(30000,result.VerseCount);

            await using(var c=new SqliteConnection($"Data Source={valid}")){await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO verse(id,book_id,chapter,verse,text) VALUES(30001,1,1,1,'duplicado');";await cmd.ExecuteNonQueryAsync();}
            result=await service.ValidateAsync(valid);
            Assert.Equal(BibleVersionValidationStatus.Incompatible,result.Status);
            Assert.Equal(1,result.DuplicateReferenceCount);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    private static async Task CreateBibleAsync(string path)
    {
        await using var c=new SqliteConnection($"Data Source={path}");await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE book(id INTEGER PRIMARY KEY,book_reference_id INTEGER,testament_reference_id INTEGER,name TEXT);
            CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT);
            CREATE TABLE verse(id INTEGER PRIMARY KEY,book_id INTEGER,chapter INTEGER,verse INTEGER,text TEXT,FOREIGN KEY(book_id) REFERENCES book(id));
            INSERT INTO metadata VALUES('dbversion','2'),('name','Teste');
            WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<66) INSERT INTO book SELECT x,x,CASE WHEN x<40 THEN 1 ELSE 2 END,'Livro '||x FROM n;
            WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<30000) INSERT INTO verse SELECT x,((x-1)%66)+1,((x-1)/1000)+1,((x-1)%1000)+1,'Texto '||x FROM n;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}

#pragma warning restore xUnit1051
