using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

if(args.Length!=3)throw new ArgumentException("Uso: IntegratedQa <pasta-isolada> <ACF.sqlite> <saída>");
var root=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BIBLIATEMA_DATA_DIR",root);
var services=new ServiceCollection();services.AddLogging();services.AddApplication().AddWebInfrastructure();await using var provider=services.BuildServiceProvider();
var importer=provider.GetRequiredService<IBibleVersionImportService>();var catalog=provider.GetRequiredService<IBibleVersionCatalogRepository>();
if(await catalog.GetByCodeAsync("QA")==null)await importer.ImportAsync(Path.GetFullPath(args[1]),"QA","Tradução QA");
await provider.GetRequiredService<IBibleVersionManager>().SetActiveVersionAsync("QA");
var themes=provider.GetRequiredService<IThemeRepository>();var links=provider.GetRequiredService<IThemeVerseLinkService>();var content=provider.GetRequiredService<IThemeContentService>();
if(!(await themes.GetAllAsync()).Any())
{
    var theme=await themes.CreateAsync("QA sequência mista","#1769AA",null);
    await links.LinkAsync(theme.Id,"QA",[new(66,1,1,1,"Nota em Apocalipse"),new(1,1,1,1,"Nota em Gênesis")],false);
    await content.SaveBlockAsync(theme.Id,null,new("Introdução\n\nFé, esperança e amor.",new(IsBold:true)));
    await content.SaveBlockAsync(theme.Id,null,new("Aplicação prática\nSegunda aplicação",new("#172033","#FFF4CC",14,TextMarkerStyle.Numbered)));
}
var reportService=provider.GetRequiredService<IReportService>();var pdf=provider.GetRequiredService<IPdfService>();var themeId=(await themes.GetAllAsync()).First().Id;
await content.SetModeAsync(themeId,ThemeOrderingMode.Canonical,true);
File.Copy(await pdf.CreateThemeVersePdfAsync(await reportService.BuildThemesAsync(new(themeId))),Path.Combine(output,"canonico.pdf"),true);
await content.SetModeAsync(themeId,ThemeOrderingMode.Manual);
var sequence=await content.GetAsync(themeId);var lastVerse=sequence.Items.Last(x=>x.ReferenceId is not null);
while((await content.GetAsync(themeId)).Items[0].Id!=lastVerse.Id)await content.MoveAsync(themeId,lastVerse.Id,-1);
var block=(await content.GetAsync(themeId)).Items.First(x=>x.TextBlock is not null);
while((await content.GetAsync(themeId)).Items[1].Id!=block.Id)await content.MoveAsync(themeId,block.Id,-1);
var manual=await reportService.BuildThemesAsync(new(themeId));File.Copy(await pdf.CreateThemeVersePdfAsync(manual),Path.Combine(output,"manual.pdf"),true);
var backup=await provider.GetRequiredService<IBackupService>().CreateAsync();File.Copy(backup.Path,Path.Combine(output,"backup-completo.zip"),true);
var backupService=provider.GetRequiredService<IBackupService>();
var beforeRestore=System.Text.Json.JsonSerializer.Serialize(await content.GetAsync(themeId));
await backupService.ValidateAsync(backup.Path);
await content.SaveBlockAsync(themeId,null,new("Alteração temporária para verificar restauração",new()));
await backupService.RestoreAsync(backup.Path);
var afterRestore=System.Text.Json.JsonSerializer.Serialize(await content.GetAsync(themeId));
if(beforeRestore!=afterRestore)throw new InvalidOperationException("A restauração não recuperou a sequência integral.");
var restored=await reportService.BuildThemesAsync(new(themeId));
File.Copy(await pdf.CreateThemeVersePdfAsync(restored),Path.Combine(output,"restaurado.pdf"),true);
Console.WriteLine("RESTORE_OK: sequência mista integral preservada; PDF regenerado após restauração.");
Console.WriteLine($"Core: {typeof(Biblia.Infrastructure.Files.BackupService).Assembly.Location}");
Console.WriteLine($"QA: {root}; {manual.ReferenceCount} referências, {manual.Sections[0].Content!.Count} itens. PDFs e ZIP: {output}");
