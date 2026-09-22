using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.Rules;
using Biblia.Infrastructure;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Files;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Biblia.Tests.Application;
#pragma warning disable xUnit1051
public sealed class IntegratedFeaturesTests
{
    [Fact]
    public async Task ExistingCanonicalThemeCharacterizationPreservesTextsObservationsVersionsAndTotals()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();
        await f.Link.LinkAsync(theme.Id,"QA",[new(66,1,1,1,"Última nota"),new(1,1,1,1,"Primeira nota")],false);
        var legacy=new ReportService(f.References,f.Themes,f.Catalog,f.Bible,f.Clock);
        var before=await legacy.BuildThemesAsync(new(theme.Id));var after=await f.Reports.BuildThemesAsync(new(theme.Id));
        Assert.Equal(before.ReferenceCount,after.ReferenceCount);Assert.Equal(before.Sections[0].References,after.Sections[0].References);
        Assert.Equal(new[]{1,66},after.Sections[0].References.Select(x=>x.BookReferenceId));
        Assert.Equal(ThemeOrderingMode.Canonical,(await f.Content.GetAsync(theme.Id)).Mode);
        Assert.Equal(new[]{0,1},(await f.Content.GetAsync(theme.Id)).Items.Select(x=>x.SortOrder));
    }
    [Theory]
    [InlineData(TextMarkerStyle.None)] [InlineData(TextMarkerStyle.Bullet)] [InlineData(TextMarkerStyle.Numbered)] [InlineData(TextMarkerStyle.Dash)] [InlineData(TextMarkerStyle.OrdinalNumbered)]
    public async Task MixedOrderSurvivesEditingReloadModeChangesBackupAndPdf(TextMarkerStyle marker)
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();
        await f.Link.LinkAsync(theme.Id,"QA",[new(66,1,1,1,"Observação em Apocalipse"),new(1,1,1,1)],false);
        await f.Content.SaveBlockAsync(theme.Id,null,new("Aplicação prática\n\nAção e fé <script>alert(1)</script>",new(MarkerStyle:marker,IsBold:true,IsItalic:true)));
        await f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Manual);
        var seq=await f.Content.GetAsync(theme.Id);var block=seq.Items.Last();await f.Content.MoveAsync(theme.Id,block.Id,-1);
        seq=await f.Content.GetAsync(theme.Id);await f.Content.MoveAsync(theme.Id,seq.Items.Last().Id,-1);await f.Content.MoveAsync(theme.Id,seq.Items.Last().Id,-1);
        seq=await f.Content.GetAsync(theme.Id);Assert.Equal(3,seq.Items.Count);Assert.Equal(ThemeOrderingMode.Manual,seq.Mode);
        var expected=seq.Items.Select(x=>x.Id).ToArray();
        await f.Link.UpdateObservationAsync(theme.Id,seq.Items.First(x=>x.ReferenceId is not null).ReferenceId!.Value,"Nota editada");
        Assert.Equal(expected,(await new ThemeContentService(f.Db).GetAsync(theme.Id)).Items.Select(x=>x.Id));
        var report=await f.Reports.BuildThemesAsync(new(theme.Id));Assert.Equal(2,report.ReferenceCount);Assert.Equal(3,report.Sections[0].Content!.Count);
        var pdf=await f.Pdf.CreateThemeVersePdfAsync(report);using(var doc=PdfDocument.Open(pdf)){var text=string.Join("\n",doc.GetPages().Select(p=>ContentOrderTextExtractor.GetText(p)));Assert.Contains("Aplicação prática",text);Assert.Contains("<script>",text);Assert.Contains("Nota editada",text);Assert.True(doc.TryGetBookmarks(out _));}
        var backup=await f.Backup.CreateAsync();await f.Content.DeleteBlockAsync(theme.Id,block.Id);await f.Backup.RestoreAsync(backup.Path);
        var restored=await f.Content.GetAsync(theme.Id);Assert.Equal(expected,restored.Items.Select(x=>x.Id));Assert.Equal(marker,restored.Items.Single(x=>x.TextBlock is not null).TextBlock!.Style.MarkerStyle);
        Assert.Equal(report.Sections[0].Content,(await f.Reports.BuildThemesAsync(new(theme.Id))).Sections[0].Content);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Canonical));
        Assert.Equal(expected,(await f.Content.GetAsync(theme.Id)).Items.Select(x=>x.Id));
        var blockIndex=restored.Items.ToList().FindIndex(x=>x.TextBlock is not null);
        await f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Canonical,true);restored=await f.Content.GetAsync(theme.Id);
        Assert.Equal(block.Id,restored.Items[blockIndex].Id);Assert.Equal(new[]{1,66},(await f.Reports.BuildThemesAsync(new(theme.Id))).Sections[0].References.Select(x=>x.BookReferenceId));
    }
    [Fact]
    public async Task ManualPublicationOrderIsPersistedAndUsedByThePdfReport()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var now=DateTimeOffset.UtcNow;
        var publication=await f.Publications.SaveAsync(new Publication(0,"Publicação de ordem manual",null,null,null,null,null,null,false,false,null,now,now));
        await f.Link.LinkToPublicationAsync(publication.Id,theme.Id,"QA",[new(66,1,1,1),new(1,1,1,1)],false);
        await f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Manual);
        await f.Content.SaveBlockAsync(theme.Id,null,new("Introdução",new()));var blockId=(await f.Content.GetAsync(theme.Id)).Items.Last().Id;
        await f.Content.MoveAsync(theme.Id,blockId,-1);await f.Content.MoveAsync(theme.Id,blockId,-1);
        var persisted=await f.Publications.GetContentAsync(publication.Id,theme.Id);
        Assert.Null(persisted[0].ReferenceId);Assert.Equal("Introdução",persisted[0].TextBlock!.Content);
        var report=await f.Reports.BuildThemesAsync(new(theme.Id,PublicationId:publication.Id));
        Assert.Collection(report.Sections.Single().Content!,item=>Assert.Equal("Introdução",item.TextBlock!.Content),item=>Assert.Equal(1,item.Reference!.BookReferenceId),item=>Assert.Equal(66,item.Reference!.BookReferenceId));
    }
    [Fact]
    public async Task AutomaticPublicationOrderIsPersistedAndUsedByThePdfReport()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var now=DateTimeOffset.UtcNow;
        var publication=await f.Publications.SaveAsync(new Publication(0,"Publicação automática",null,null,null,null,null,null,false,false,null,now,now));
        await f.Link.LinkToPublicationAsync(publication.Id,theme.Id,"QA",[new(66,1,1,1),new(1,1,1,1)],false);
        var persisted=await f.Publications.GetContentAsync(publication.Id,theme.Id);
        var genesis=await f.References.FindCanonicalAsync(1,1,1,1);
        Assert.Equal(genesis!.Id,persisted[0].ReferenceId);
        var report=await f.Reports.BuildThemesAsync(new(theme.Id,PublicationId:publication.Id));
        Assert.Equal(new[]{1,66},report.Sections.Single().Content!.Select(x=>x.Reference!.BookReferenceId));
    }
    [Fact]
    public async Task PublicationLinkPersistsObservationForNewAndPreviouslyLinkedVerse()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var otherTheme=await f.Themes.CreateAsync("Tema independente","#1769AA",null);var now=DateTimeOffset.UtcNow;
        var publication=await f.Publications.SaveAsync(new Publication(0,"Publicação com observação",null,null,null,null,null,null,false,false,null,now,now));

        await f.Link.LinkToPublicationAsync(publication.Id,theme.Id,"QA",[new(1,1,1,1,"Nota inicial")],false);
        var first=(await f.Link.GetLinkedAsync(theme.Id,"QA")).Single();
        Assert.Equal("Nota inicial",first.Observation);

        await f.Link.LinkToPublicationAsync(publication.Id,theme.Id,"QA",[new(1,1,1,1,"Nota atualizada")],false);
        var linked=(await f.Link.GetLinkedAsync(theme.Id,"QA")).Single();
        Assert.Equal("Nota atualizada",linked.Observation);

        var report=await f.Reports.BuildThemesAsync(new(theme.Id,PublicationId:publication.Id));
        Assert.Equal("Nota atualizada",report.Sections.Single().References.Single().Observation);

        await f.Link.LinkAsync(otherTheme.Id,"QA",[new(1,1,1,1,"Nota do outro tema")],false);
        Assert.Equal("Nota atualizada",(await f.Link.GetLinkedAsync(theme.Id,"QA")).Single().Observation);
        Assert.Equal("Nota do outro tema",(await f.Link.GetLinkedAsync(otherTheme.Id,"QA")).Single().Observation);
    }
    [Fact]
    public async Task EmptyObservationNeverReceivesThemeTextBlockContent()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var now=DateTimeOffset.UtcNow;
        var publication=await f.Publications.SaveAsync(new Publication(0,"Publicação sem observação",null,null,null,null,null,null,false,false,null,now,now));
        const string blockContent="9º Seja um Exemplo Para que as Outras Pessoas Possam Seguir";

        await f.Content.SaveBlockAsync(theme.Id,null,new(blockContent,new()));
        await f.Link.LinkToPublicationAsync(publication.Id,theme.Id,"QA",[new(54,4,12,12)],false);
        await f.Publications.SynchronizeLegacyThemeContentAsync(publication.Id,theme.Id);

        var linked=Assert.Single(await f.Link.GetLinkedAsync(theme.Id,"QA"));
        Assert.Null(linked.Observation);

        var stored=Assert.Single(await f.References.GetThemeLinksAsync(theme.Id));
        Assert.Null(stored.Observation);

        var report=await f.Reports.BuildThemesAsync(new(theme.Id,PublicationId:publication.Id));
        var section=Assert.Single(report.Sections);
        Assert.Null(Assert.Single(section.References).Observation);
        Assert.DoesNotContain(section.Content!,item=>item.TextBlock?.Content==blockContent);

        // Blocos globais legados não fazem parte da publicação e, portanto, não interferem
        // na observação armazenada para o vínculo PublicationId + ThemeId.
    }
    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("too-long")]
    public async Task RejectInvalidBlockText(string text)
    {
        await using var f=await Fixture.Create();var t=await f.Theme();if(text=="too-long")text=new string('x',10001);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Content.SaveBlockAsync(t.Id,null,new(text,new())));Assert.Empty((await f.Content.GetAsync(t.Id)).Items);
    }
    [Theory]
    [InlineData("red","#FFFFFF",11)] [InlineData("#000000","#FFFFFF",6)] [InlineData("#000000","#FFFFFF",25)]
    public void ValidateStyle(string foreground,string background,double size)=>Assert.Throws<ArgumentException>(()=>new ThemeTextStyle(foreground,background,size).Validate());
    [Fact]
    public void ValidateStyleAllowsAnyValidColorCombination()=>new ThemeTextStyle("#FFFFFF","#FFFFFF",11).Validate();
    [Fact]
    public async Task MoveBoundsConcurrentMovesAndCascadeDeleteMaintainUniquePositions()
    {
        await using var f=await Fixture.Create();var t=await f.Theme();
        for(var i=0;i<4;i++)await f.Content.SaveBlockAsync(t.Id,null,new("Bloco "+i,new()));
        var list=(await f.Content.GetAsync(t.Id)).Items;await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Content.MoveAsync(t.Id,list[1].Id,-1));
        await f.Content.SetModeAsync(t.Id,ThemeOrderingMode.Manual);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Content.MoveAsync(t.Id,list[0].Id,-1));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Content.MoveAsync(t.Id,list[^1].Id,1));
        await Task.WhenAll(f.Content.MoveAsync(t.Id,list[1].Id,-1),f.Content.MoveAsync(t.Id,list[2].Id,1));
        list=(await f.Content.GetAsync(t.Id)).Items;Assert.Equal(new[]{0,1,2,3},list.Select(x=>x.SortOrder));
        await f.Content.SaveBlockAsync(t.Id,list[1].Id,new("Editado\nUnicode: bênção",new()));Assert.Equal(list.Select(x=>x.Id),(await f.Content.GetAsync(t.Id)).Items.Select(x=>x.Id));
        await f.Content.DeleteBlockAsync(t.Id,list[1].Id);Assert.Equal(new[]{0,1,2},(await f.Content.GetAsync(t.Id)).Items.Select(x=>x.SortOrder));
        await f.Themes.DeleteAsync(t.Id);await using var c=await f.Db.OpenConnectionAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM ThemeContent;";Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }
    [Theory]
    [InlineData(".sqlite")] [InlineData(".db")]
    public async Task ImportActivateRemoveAndProtectLinkedVersion(string ext)
    {
        await using var f=await Fixture.Create();var file=Path.Combine(f.Root,"upload"+ext);File.Copy(f.Source,file);var before=SHA256.HashData(await File.ReadAllBytesAsync(file));
        var result=await f.Import.ImportAsync(file,"NEW","Nova tradução");Assert.True(result.Succeeded);Assert.Equal(Convert.ToHexString(before),result.Sha256);Assert.Equal(before,SHA256.HashData(await File.ReadAllBytesAsync(file)));
        Assert.NotNull(await f.Catalog.GetByCodeAsync("NEW"));await f.Manager.SetActiveVersionAsync("NEW");Assert.Equal("NEW",(await f.Manager.GetActiveVersionAsync())!.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Import.ImportAsync(file,"NEW","Duplicada"));
        var t=await f.Theme();await f.Link.LinkAsync(t.Id,"NEW",[new(1,1,1,1)],false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Import.RemoveImportedAsync("NEW"));Assert.True(File.Exists(result.Version!.InstalledPath));
        var id=(await f.Content.GetAsync(t.Id)).Items.Single().ReferenceId!.Value;await f.Link.UnlinkAsync(t.Id,id);await f.Import.RemoveImportedAsync("NEW");Assert.Null(await f.Catalog.GetByCodeAsync("NEW"));
    }
    [Theory]
    [InlineData("extension")] [InlineData("size")] [InlineData("corrupt")] [InlineData("cancel")]
    public async Task ImportRejectsBadInputsAndCleansTemporaryFiles(string kind)
    {
        await using var f=await Fixture.Create();var file=Path.Combine(f.Root,"bad"+(kind=="extension"?".txt":".db"));await File.WriteAllTextAsync(file,"invalid");
        if(kind=="size"){using var s=File.OpenWrite(file);s.SetLength(256L*1024*1024+1);}
        if(kind=="corrupt"){Assert.False((await f.Import.ImportAsync(file,"BAD","Ruim")).Succeeded);}
        else if(kind=="cancel"){using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Import.ImportAsync(file,"BAD","Ruim",cts.Token));}
        else await Assert.ThrowsAsync<InvalidDataException>(()=>f.Import.ImportAsync(file,"BAD","Ruim"));
        Assert.Empty(Directory.GetFiles(f.Root,"*.tmp",SearchOption.AllDirectories));Assert.Null(await f.Catalog.GetByCodeAsync("BAD"));
    }
    [Fact]
    public async Task BackupIncludesEveryInstalledFileAndRestoresPortablePaths()
    {
        await using var f=await Fixture.Create();await f.Import.ImportAsync(f.Source,"SECOND","Segunda");await f.Manager.SetActiveVersionAsync("SECOND");
        var b=await f.Backup.CreateAsync();using(var z=ZipFile.OpenRead(b.Path)){Assert.Equal(4,z.Entries.Count);await using var stream=z.GetEntry("manifest.json")!.Open();var m=(await JsonSerializer.DeserializeAsync<BackupService.Manifest>(stream))!;Assert.Equal(2,m.BackupFormatVersion);Assert.Equal(2,m.Bibles!.Count);foreach(var x in m.Bibles){var entry=z.GetEntry(x.EntryName)!;Assert.Equal(x.Size,entry.Length);await using var data=entry.Open();Assert.Equal(x.Sha256,Convert.ToHexString(await SHA256.HashDataAsync(data)));}}
        await f.Manager.SetActiveVersionAsync("QA");await f.Import.RemoveImportedAsync("SECOND");await f.Backup.RestoreAsync(b.Path);
        Assert.Equal("SECOND",(await f.Manager.GetActiveVersionAsync())!.Code);var installed=await f.Catalog.GetAllAsync();foreach(var x in installed.Where(x=>x.IsInstalled)){Assert.StartsWith(Path.Combine(f.Root,"Bibles","Restored-"),x.InstalledPath);Assert.NotEmpty((await f.Bible.GetPassageAsync(x.Code,1,1,1,1)).Verses);}
        Assert.True(Directory.GetFiles(Path.Combine(f.Root,"backups"),"*.zip").Length>=2);
    }
    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("path")] [InlineData("hash")] [InlineData("size")] [InlineData("bomb")]
    public async Task BackupRejectsTamperingBeforeStateChange(string kind)
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var b=await f.Backup.CreateAsync();var file=Path.Combine(f.Root,"tampered.zip");File.Copy(b.Path,file);
        using(var zip=ZipFile.Open(file,ZipArchiveMode.Update))
        {
            if(kind=="missing")zip.GetEntry("bibles/QA.sqlite")!.Delete();
            if(kind=="duplicate")await Write(zip,"bibliatema.db","duplicate");
            if(kind=="path")await Write(zip,"../escape.db","path");
            if(kind=="bomb")await Write(zip,"bomb",new string('0',2_000_000));
            if(kind is "hash" or "size")
            {
                var entry=zip.GetEntry("manifest.json")!;BackupService.Manifest m;using(var s=entry.Open())m=JsonSerializer.Deserialize<BackupService.Manifest>(s)!;entry.Delete();m=kind=="hash"?m with{DatabaseSha256=new string('0',64)}:m with{DatabaseSize=1};await Write(zip,"manifest.json",JsonSerializer.Serialize(m));
            }
        }
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Backup.RestoreAsync(file));Assert.Equal(theme.Name,(await f.Themes.GetAllAsync()).Single().Name);
    }
    [Fact]
    public async Task LongTextBlockPaginatesAndPreservesFinalLine()
    {
        await using var f=await Fixture.Create();var t=await f.Theme();await f.Content.SaveBlockAsync(t.Id,null,new(string.Join('\n',Enumerable.Range(1,180).Select(i=>$"Parágrafo {i}: fé, amor e esperança.")),new(FontSize:24,MarkerStyle:TextMarkerStyle.Numbered)));
        var r=await f.Reports.BuildThemesAsync(new(t.Id));Assert.Equal(0,r.ReferenceCount);var path=await f.Pdf.CreateThemeVersePdfAsync(r);using var pdf=PdfDocument.Open(path);Assert.True(pdf.NumberOfPages>3);Assert.Contains("Parágrafo 180",string.Join("\n",pdf.GetPages().Select(p=>ContentOrderTextExtractor.GetText(p))));
    }
    private static async Task Write(ZipArchive zip,string name,string content){await using var s=zip.CreateEntry(name).Open();await using var w=new StreamWriter(s);await w.WriteAsync(content);}
    [Fact]
    public async Task MoreThanTwentyEntriesAreValidWithinLimits()
    {
        await using var f=await Fixture.Create();var original=(await f.Catalog.GetByCodeAsync("QA"))!;
        for(var i=0;i<20;i++)await f.Catalog.CreateAsync(original with{Id=0,Code=$"Q{i:00}",DisplayName=$"Versão {i}"});
        var b=await f.Backup.CreateAsync();using(var z=ZipFile.OpenRead(b.Path))Assert.Equal(23,z.Entries.Count);
        Assert.Equal(2,(await f.Backup.ValidateAsync(b.Path)).BackupFormatVersion);
    }
    [Fact]
    public async Task LegacyBackupPreservesAdditionalLocalVersionsAndResolvesPaths()
    {
        await using var f=await Fixture.Create();var t=await f.Theme();var b=await f.Backup.CreateAsync();var old=Path.Combine(f.Root,"legacy.zip");
        using(var source=ZipFile.OpenRead(b.Path))using(var target=ZipFile.Open(old,ZipArchiveMode.Create))
        {
            using(var input=source.GetEntry("bibliatema.db")!.Open())using(var output=target.CreateEntry("bibliatema.db").Open())await input.CopyToAsync(output);
            await Write(target,"manifest.json",JsonSerializer.Serialize(new{CreatedAt=DateTimeOffset.UtcNow,SchemaVersion=AppDatabase.CurrentSchemaVersion}));
        }
        var extra=await f.Import.ImportAsync(f.Source,"EXTRA","Extra local");await f.Backup.RestoreAsync(old);
        Assert.True(File.Exists(extra.Version!.InstalledPath));Assert.NotNull(await f.Catalog.GetByCodeAsync("EXTRA"));Assert.Equal(t.Name,(await f.Themes.GetAllAsync()).Single().Name);
    }
    [Fact]
    public async Task FailureAfterDatabaseSwitchRollsBackEverything()
    {
        await using var f=await Fixture.Create();var b=await f.Backup.CreateAsync();var t=await f.Theme();var version=(await f.Catalog.GetByCodeAsync("QA"))!;
        var failing=new BackupService(new FailAfterSwitch(f.Db),new Paths(f.Root),NullLogger<BackupService>.Instance);
        await Assert.ThrowsAsync<IOException>(()=>failing.RestoreAsync(b.Path));
        Assert.Equal(t.Name,(await f.Themes.GetAllAsync()).Single().Name);Assert.Equal(version.InstalledPath,(await f.Catalog.GetByCodeAsync("QA"))!.InstalledPath);Assert.Empty(Directory.GetDirectories(Path.Combine(f.Root,"Bibles"),"Restored-*"));
    }
    [Theory]
    [InlineData("format")] [InlineData("schema")] [InlineData("active")] [InlineData("disabled-active")]
    [InlineData("duplicate-code")] [InlineData("bible-hash")] [InlineData("catalog-name")]
    public async Task RejectInconsistentManifestWithoutChangingCurrentState(string kind)
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var backup=await f.Backup.CreateAsync();
        using(var zip=ZipFile.Open(backup.Path,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("manifest.json")!;BackupService.Manifest m;
            using(var input=entry.Open())m=JsonSerializer.Deserialize<BackupService.Manifest>(input)!;
            var bible=m.Bibles!.Single();
            m=kind switch
            {
                "format"=>m with{BackupFormatVersion=99},
                "schema"=>m with{SchemaVersion=999},
                "active"=>m with{Bibles=[bible with{IsActive=false}]},
                "disabled-active"=>m with{Bibles=[bible with{IsEnabled=false}]},
                "duplicate-code"=>m with{Bibles=[bible,bible]},
                "bible-hash"=>m with{Bibles=[bible with{Sha256=new string('0',64)}]},
                _=>m with{Bibles=[bible with{DisplayName="Nome divergente"}]}
            };
            entry.Delete();await Write(zip,"manifest.json",JsonSerializer.Serialize(m));
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Backup.RestoreAsync(backup.Path));
        Assert.Equal(theme.Name,(await f.Themes.GetAllAsync()).Single().Name);
        Assert.Equal("QA",(await f.Manager.GetActiveVersionAsync())!.Code);
        Assert.NotEmpty((await f.Bible.GetPassageAsync("QA",1,1,1,1)).Verses);
        Assert.Empty(Directory.GetDirectories(Path.Combine(f.Root,"Cache"),"backup-stage-*"));
    }
    [Fact]
    public async Task CancelledBackupOperationsPreserveStateAndReleaseLocks()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();var backup=await f.Backup.CreateAsync();
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Backup.CreateAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Backup.ValidateAsync(backup.Path,cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Backup.RestoreAsync(backup.Path,cancelled.Token));
        Assert.Equal(theme.Name,(await f.Themes.GetAllAsync()).Single().Name);
        Assert.Equal(2,(await f.Backup.ValidateAsync((await f.Backup.CreateAsync()).Path)).BackupFormatVersion);
    }
    [Fact]
    public async Task CrossThemeOperationsCannotModifyAnotherThemesBlocks()
    {
        await using var f=await Fixture.Create();var first=await f.Theme();var second=await f.Themes.CreateAsync("Outro tema","#1769AA",null);
        await f.Content.SaveBlockAsync(first.Id,null,new("Bloco protegido",new()));
        var item=(await f.Content.GetAsync(first.Id)).Items.Single();await f.Content.SetModeAsync(second.Id,ThemeOrderingMode.Manual);
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Content.SaveBlockAsync(second.Id,item.Id,new("Alterado",new())));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Content.DeleteBlockAsync(second.Id,item.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>f.Content.MoveAsync(second.Id,item.Id,1));
        Assert.Equal("Bloco protegido",(await f.Content.GetAsync(first.Id)).Items.Single().TextBlock!.Content);
        Assert.Empty((await f.Content.GetAsync(second.Id)).Items);
    }
    [Fact]
    public async Task CompleteRestoreExcludesExtraVersionButKeepsItsFileForRecovery()
    {
        await using var f=await Fixture.Create();var backup=await f.Backup.CreateAsync();
        var extra=await f.Import.ImportAsync(f.Source,"EXTRA","Extra posterior");
        await f.Backup.RestoreAsync(backup.Path);
        Assert.Null(await f.Catalog.GetByCodeAsync("EXTRA"));Assert.True(File.Exists(extra.Version!.InstalledPath));
        Assert.NotEmpty((await f.Bible.GetPassageAsync("QA",1,1,1,1)).Verses);
    }
    private sealed class FailAfterSwitch(IAppDatabase db):IAppDatabase
    {
        int calls;public string DatabasePath=>db.DatabasePath;public Task InitializeAsync(CancellationToken ct=default)=>db.InitializeAsync(ct);public Task<int> GetSchemaVersionAsync(CancellationToken ct=default)=>db.GetSchemaVersionAsync(ct);
        public async Task ReplaceAsync(string path,CancellationToken ct=default){await db.ReplaceAsync(path,ct);if(calls++==0)throw new IOException("Falha simulada após troca");}
    }
    [Fact]
    public async Task DerivedNumbersSurviveMutationsFailureReloadAndRestore()
    {
        await using var f=await Fixture.Create();var theme=await f.Theme();
        await f.Link.LinkAsync(theme.Id,"QA",[new(66,1,1,1),new(1,1,1,1)],false);
        foreach(var marker in new[]{TextMarkerStyle.Numbered,TextMarkerStyle.Bullet,TextMarkerStyle.OrdinalNumbered,TextMarkerStyle.Dash,TextMarkerStyle.None,TextMarkerStyle.Numbered})
            await f.Content.SaveBlockAsync(theme.Id,null,new("Texto multilinha\nSegunda linha",new(MarkerStyle:marker)));
        async Task<string[]> Prefixes()=>ThemeBlockMarkers.Apply((await f.Content.GetAsync(theme.Id)).Items,x=>x.TextBlock).Select(x=>x.Prefix).ToArray();
        Assert.Equal(new[]{"","","1.","•","2º","–","","3."},await Prefixes());
        await f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Manual);
        var last=(await f.Content.GetAsync(theme.Id)).Items.Last();
        for(var i=0;i<6;i++)await f.Content.MoveAsync(theme.Id,last.Id,-1);
        Assert.Equal(new[]{"","1.","","2.","•","3º","–",""},await Prefixes());
        await f.Content.SaveBlockAsync(theme.Id,last.Id,last.TextBlock! with{Style=last.TextBlock!.Style with{MarkerStyle=TextMarkerStyle.None}});
        Assert.Equal(new[]{"","","","1.","•","2º","–",""},await Prefixes());
        var firstNumber=(await f.Content.GetAsync(theme.Id)).Items.First(x=>x.TextBlock?.Style.MarkerStyle==TextMarkerStyle.Numbered);
        await f.Content.DeleteBlockAsync(theme.Id,firstNumber.Id);
        Assert.Equal(new[]{"","","","•","1º","–",""},await Prefixes());
        var before=await Prefixes();var beforeItems=(await f.Content.GetAsync(theme.Id)).Items;
        await using(var c=await f.Db.OpenConnectionAsync()){
            await using var command=c.CreateCommand();command.CommandText="CREATE TRIGGER qa_fail_order BEFORE UPDATE OF SortOrder ON ThemeContent BEGIN SELECT RAISE(ABORT, 'QA order failure'); END;";await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(()=>f.Content.MoveAsync(theme.Id,last.Id,-1));
        await using(var c=await f.Db.OpenConnectionAsync()){
            await using var command=c.CreateCommand();command.CommandText="DROP TRIGGER qa_fail_order;";await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(before,await Prefixes());Assert.Equal(beforeItems,(await new ThemeContentService(f.Db).GetAsync(theme.Id)).Items);
        var backup=await f.Backup.CreateAsync();
        await f.Content.SaveBlockAsync(theme.Id,null,new("Mais um",new(MarkerStyle:TextMarkerStyle.Numbered)));
        Assert.Equal("2.",(await Prefixes()).Last());await f.Backup.RestoreAsync(backup.Path);Assert.Equal(before,await Prefixes());
        await f.Content.SetModeAsync(theme.Id,ThemeOrderingMode.Canonical,true);Assert.Equal(before,await Prefixes());
        var report=await f.Reports.BuildThemesAsync(new(theme.Id));
        Assert.Equal(before,ThemeBlockMarkers.Apply(report.Sections[0].Content!,x=>x.TextBlock).Select(x=>x.Prefix));
    }
    private sealed class Fixture:IAsyncDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"Biblia.Integrated",Guid.NewGuid().ToString("N"));public string Source=>Path.Combine(CanonicalThemeFlowTests.FindBibles(),"ACF.sqlite");
        public ServiceProvider Services=null!;public IClock Clock=>Services.GetRequiredService<IClock>();public AppDatabase Db=>Services.GetRequiredService<AppDatabase>();public IThemeRepository Themes=>Services.GetRequiredService<IThemeRepository>();public IBibleVersionCatalogRepository Catalog=>Services.GetRequiredService<IBibleVersionCatalogRepository>();public ISavedReferenceRepository References=>Services.GetRequiredService<ISavedReferenceRepository>();public IThemeVerseLinkService Link=>Services.GetRequiredService<IThemeVerseLinkService>();public IThemeContentService Content=>Services.GetRequiredService<IThemeContentService>();public IPublicationService Publications=>Services.GetRequiredService<IPublicationService>();public IReportService Reports=>Services.GetRequiredService<IReportService>();public IPdfService Pdf=>Services.GetRequiredService<IPdfService>();public IBibleRepository Bible=>Services.GetRequiredService<IBibleRepository>();public IBibleVersionImportService Import=>Services.GetRequiredService<IBibleVersionImportService>();public IBibleVersionManager Manager=>Services.GetRequiredService<IBibleVersionManager>();public IBackupService Backup=>Services.GetRequiredService<IBackupService>();
        public static async Task<Fixture> Create(){var f=new Fixture();Directory.CreateDirectory(f.Root);var services=new ServiceCollection();services.AddLogging();services.AddApplication().AddWebInfrastructure();services.AddSingleton<IAppPaths>(new Paths(f.Root));services.AddSingleton<IBibleVersionManifestProvider>(new ManifestProvider());f.Services=services.BuildServiceProvider();await f.Db.InitializeAsync();var result=await f.Import.ImportAsync(f.Source,"QA","Bíblia QA");Assert.True(result.Succeeded);await f.Manager.SetActiveVersionAsync("QA");return f;}
        public Task<Theme> Theme()=>Themes.CreateAsync("Tema de teste","#1769AA",null);
        public async ValueTask DisposeAsync(){await Services.DisposeAsync();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>Path.Combine(root,"Cache");public string GetPrivateFilePath(string name)=>Path.Combine(root,name);}
    private sealed class ManifestProvider:IBibleVersionManifestProvider{public Task<BibleVersionManifest> GetManifestAsync(CancellationToken ct=default)=>Task.FromResult(new BibleVersionManifest(1,"QA",[]));}
}
