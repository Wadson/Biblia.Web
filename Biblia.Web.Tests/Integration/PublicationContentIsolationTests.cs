using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biblia.Tests.Integration;

#pragma warning disable xUnit1051
public sealed class PublicationContentIsolationTests
{
    [Fact]
    public async Task SameTheme_StoresReferencesObservationsVersionsBlocksAndOrderPerPublication()
    {
        var root=Path.Combine(Path.GetTempPath(),"BibliaTema.Isolation",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db=new AppDatabase(Path.Combine(root,"app.db"),NullLogger<AppDatabase>.Instance); await db.InitializeAsync();
            var clock=new TestClock(); var publications=new PublicationService(db,clock); var themes=new ThemeRepository(db,clock); var references=new SavedReferenceRepository(db,clock); var versions=new BibleVersionCatalogRepository(db);
            var theme=await themes.CreateAsync("Fé",null,null);
            var a=await publications.SaveAsync(Publication("A")); var b=await publications.SaveAsync(Publication("B"));
            await publications.LinkThemeAsync(a.Id,theme.Id); await publications.LinkThemeAsync(b.Id,theme.Id);
            var acf=await versions.CreateAsync(Version("ACF")); var nvi=await versions.CreateAsync(Version("NVI"));
            var reference=await references.CreateAsync(new(0,43,3,16,16,null,acf.Id,default,default)); await references.AddThemeAsync(reference.Id,theme.Id);

            await publications.AddReferenceAsync(a.Id,theme.Id,reference.Id,acf.Id,"Comentário A");
            await publications.AddReferenceAsync(b.Id,theme.Id,reference.Id,nvi.Id,"Comentário B");
            var blockA=await publications.SaveTextBlockAsync(a.Id,theme.Id,null,new("Introdução A",new()));
            await publications.SaveTextBlockAsync(b.Id,theme.Id,null,new("Introdução B",new()));

            var contentA=await publications.GetContentAsync(a.Id,theme.Id); var contentB=await publications.GetContentAsync(b.Id,theme.Id);
            var referenceA=Assert.Single(contentA,x=>x.ReferenceId==reference.Id); var referenceB=Assert.Single(contentB,x=>x.ReferenceId==reference.Id);
            Assert.Equal("Comentário A",referenceA.Observation); Assert.Equal(acf.Id,referenceA.BibleVersionId);
            Assert.Equal("Comentário B",referenceB.Observation); Assert.Equal(nvi.Id,referenceB.BibleVersionId);
            Assert.Contains(contentA,x=>x.TextBlock?.Content=="Introdução A"); Assert.DoesNotContain(contentA,x=>x.TextBlock?.Content=="Introdução B");
            Assert.Contains(contentB,x=>x.TextBlock?.Content=="Introdução B"); Assert.DoesNotContain(contentB,x=>x.TextBlock?.Content=="Introdução A");

            await publications.UpdateReferenceObservationAsync(a.Id,theme.Id,reference.Id,"Alterado A");
            await publications.MoveContentAsync(a.Id,theme.Id,blockA,-1);
            await publications.RemoveReferenceAsync(a.Id,theme.Id,reference.Id);
            Assert.DoesNotContain(await publications.GetContentAsync(a.Id,theme.Id),x=>x.ReferenceId==reference.Id);
            Assert.Equal("Comentário B",Assert.Single(await publications.GetContentAsync(b.Id,theme.Id),x=>x.ReferenceId==reference.Id).Observation);
            Assert.NotNull(await references.GetAsync(reference.Id));

            await publications.DeleteAsync(a.Id);
            Assert.NotNull(await publications.GetAsync(b.Id));
            Assert.Single(await publications.GetContentAsync(b.Id,theme.Id),x=>x.ReferenceId==reference.Id);
            Assert.NotNull(await themes.GetAsync(theme.Id));
        }
        finally { SqliteConnection.ClearAllPools(); if(Directory.Exists(root))Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Migration10_AddsPublicationScopedColumnsAndIndexes()
    {
        var root=Path.Combine(Path.GetTempPath(),"BibliaTema.Migration",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db=new AppDatabase(Path.Combine(root,"app.db"),NullLogger<AppDatabase>.Instance); await db.InitializeAsync();
            Assert.Equal(10,await db.GetSchemaVersionAsync());
            await using var connection=await db.OpenConnectionAsync(); await using var command=connection.CreateCommand();
            command.CommandText="PRAGMA table_info(PublicationContent);"; await using var reader=await command.ExecuteReaderAsync(); var columns=new List<string>(); while(await reader.ReadAsync())columns.Add(reader.GetString(1));
            Assert.Contains("Observation",columns); Assert.Contains("BibleVersionId",columns);
        }
        finally { SqliteConnection.ClearAllPools(); if(Directory.Exists(root))Directory.Delete(root,true); }
    }

    private static Publication Publication(string name)=>new(0,name,null,null,null,null,null,null,false,false,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
    private static BibleVersionCatalogEntry Version(string code)=>new(0,code,code,"pt-BR",$"{code}.sqlite",$"{code}.sqlite",2,null,null,null,true,true,true,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,BibleVersionValidationStatus.Compatible,null);
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow; }
}
#pragma warning restore xUnit1051
