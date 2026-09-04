using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.Rules;
namespace Biblia.Application.Services;
public sealed class ReportService(ISavedReferenceRepository references,IThemeRepository themes,IBibleVersionCatalogRepository versions,IBibleRepository bible,IClock clock):IReportService
{
 public async Task<ReportsOverview> GetOverviewAsync(CancellationToken ct=default){var refs=await references.SearchAsync(null,ct);return new((await themes.GetAllAsync(ct)).Count,refs.Count,refs.Count(x=>!string.IsNullOrWhiteSpace(x.Reference.Comment)),await references.CountThemeLinksAsync(ct));}
 public async Task<ThemeVerseReport> BuildThemesAsync(ThemeVerseReportRequest request,CancellationToken ct=default)
 {
  var version=await versions.GetByCodeAsync(request.BibleVersionCode,ct)??throw new InvalidOperationException("A versão bíblica selecionada não existe.");
  if(!version.IsInstalled||!version.IsEnabled||version.ValidationStatus==BibleVersionValidationStatus.Incompatible)throw new InvalidOperationException("A versão bíblica selecionada não está disponível.");
  var selected=(await themes.GetAllAsync(ct)).Where(x=>request.ThemeId is null||x.Id==request.ThemeId).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
  if(request.ThemeId is not null&&selected.Length==0)throw new KeyNotFoundException("Tema não encontrado.");
  var books=(await bible.GetBooksAsync(version.Code,ct)).ToDictionary(x=>x.BookReferenceId,x=>x.Name);var sections=new List<ThemeVerseReportSection>();
  foreach(var theme in selected){var items=new List<ThemeVerseReportReference>();foreach(var link in await references.GetThemeLinksAsync(theme.Id,ct)){var saved=await references.GetAsync(link.ReferenceId,ct);if(saved is null)continue;var passage=await bible.GetPassageAsync(version.Code,saved.BookReferenceId,saved.Chapter,saved.VerseStart,saved.VerseEnd,ct);var book=books.GetValueOrDefault(saved.BookReferenceId,$"Livro {saved.BookReferenceId}");items.Add(new(saved.Id,BibleReferenceFormatter.Format(book,saved.Chapter,saved.VerseStart,saved.VerseEnd),string.Join(" ",passage.Verses.Select(x=>$"{x.Verse} {x.Text}")),link.Observation,saved.BookReferenceId,saved.Chapter,saved.VerseStart,saved.VerseEnd));}sections.Add(new(theme,items.OrderBy(x=>x.BookReferenceId).ThenBy(x=>x.Chapter).ThenBy(x=>x.VerseStart).ToArray()));}
  return new("Temas e versículos",$"{version.Code} - {version.DisplayName}",clock.UtcNow,sections.Count,sections.Sum(x=>x.References.Count),sections);
 }
}
