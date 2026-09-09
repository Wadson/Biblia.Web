using System.Text.Json;
using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Rules;
using Biblia.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

if(args.Length!=3)throw new ArgumentException("MarkerQa <dados isolados> <ACF.sqlite> <saida>");
var root=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BIBLIATEMA_DATA_DIR",root);
var services=new ServiceCollection();services.AddLogging();services.AddApplication().AddWebInfrastructure();await using var provider=services.BuildServiceProvider();
var catalog=provider.GetRequiredService<IBibleVersionCatalogRepository>();
if(await catalog.GetByCodeAsync("QA")==null)await provider.GetRequiredService<IBibleVersionImportService>().ImportAsync(Path.GetFullPath(args[1]),"QA","Tradução QA");
await provider.GetRequiredService<IBibleVersionManager>().SetActiveVersionAsync("QA");
var themes=provider.GetRequiredService<IThemeRepository>();var content=provider.GetRequiredService<IThemeContentService>();var links=provider.GetRequiredService<IThemeVerseLinkService>();
var theme=(await themes.GetAllAsync()).FirstOrDefault()??await themes.CreateAsync("QA marcadores e sequência","#1769AA",null);
if((await content.GetAsync(theme.Id)).Items.Count==0){
    await links.LinkAsync(theme.Id,"QA",[new(66,1,1,1,"Observação preservada"),new(1,1,1,1,"Primeira referência")],false);
    ThemeTextBlock[] blocks=[
        new("Introdução sem marcador\nFé, esperança e amor.",new()),
        new("Primeiro ensino\nSegunda linha do mesmo bloco.\n\nOutro parágrafo alinhado.",new("#172033","#FFF4CC",14,TextMarkerStyle.Numbered,true)),
        new("Aplicação complementar\nUm ponto para o bloco inteiro.",new(MarkerStyle:TextMarkerStyle.Bullet)),
        new("Segundo ensino\nA numeração ordinal compartilha a sequência.",new("#FFFFFF","#174F7D",13,TextMarkerStyle.OrdinalNumbered,false,true)),
        new("Observação com traço\nSegunda linha sem repetir o traço.",new(MarkerStyle:TextMarkerStyle.Dash)),
        new("Conclusão\nPratique o amor ao próximo.",new(MarkerStyle:TextMarkerStyle.Numbered)),
        new(string.Join("\n",Enumerable.Range(1,60).Select(i=>$"Parágrafo longo {i:D2}: ação, bênção e perseverança.")),new(MarkerStyle:TextMarkerStyle.OrdinalNumbered))
    ];
    foreach(var block in blocks)await content.SaveBlockAsync(theme.Id,null,block);
    await content.SetModeAsync(theme.Id,ThemeOrderingMode.Manual);
    var seq=await content.GetAsync(theme.Id);var introduction=seq.Items[2];
    await content.MoveAsync(theme.Id,introduction.Id,-1);await content.MoveAsync(theme.Id,introduction.Id,-1);
    var teaching=(await content.GetAsync(theme.Id)).Items[3];await content.MoveAsync(theme.Id,teaching.Id,-1);
    await content.MoveAsync(theme.Id,teaching.Id,1);await content.MoveAsync(theme.Id,teaching.Id,-1);
}
var reports=provider.GetRequiredService<IReportService>();var pdf=provider.GetRequiredService<IPdfService>();
async Task Export(string name){
    var report=await reports.BuildThemesAsync(new(theme.Id));
    File.Copy(await pdf.CreateThemeVersePdfAsync(report),Path.Combine(output,name+".pdf"),true);
    await File.WriteAllTextAsync(Path.Combine(output,name+".json"),JsonSerializer.Serialize(ThemeBlockMarkers.Apply(report.Sections[0].Content!,x=>x.TextBlock),new JsonSerializerOptions{WriteIndented=true}));
}
await Export("manual");await content.SetModeAsync(theme.Id,ThemeOrderingMode.Canonical,true);await Export("canonico");
await content.SetModeAsync(theme.Id,ThemeOrderingMode.Manual);
var backup=await provider.GetRequiredService<IBackupService>().CreateAsync();File.Copy(backup.Path,Path.Combine(output,"backup-qa.zip"),true);
Console.WriteLine($"QA pronta: tema {theme.Id}, {root}; Core: {typeof(ThemeBlockMarkers).Assembly.Location}");
