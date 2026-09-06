using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.Rules;

namespace Biblia.Application.Services;

public sealed class ReportService(
    ISavedReferenceRepository references,
    IThemeRepository themes,
    IBibleVersionCatalogRepository versions,
    IBibleRepository bible,
    IClock clock) : IReportService
{
    public async Task<ReportsOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var items = await references.SearchAsync(null, ct);
        return new((await themes.GetAllAsync(ct)).Count, items.Count,
            items.Count(x => !string.IsNullOrWhiteSpace(x.Reference.Comment)), await references.CountThemeLinksAsync(ct));
    }

    public async Task<ThemeVerseReport> BuildThemesAsync(ThemeVerseReportRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var version = await versions.GetByCodeAsync(request.BibleVersionCode, ct)
            ?? throw new InvalidOperationException("A versão bíblica selecionada não existe.");
        if (!version.IsInstalled || !version.IsEnabled || version.ValidationStatus == BibleVersionValidationStatus.Incompatible)
            throw new InvalidOperationException("A versão bíblica selecionada não está disponível.");

        var selected = (await themes.GetAllAsync(ct))
            .Where(x => request.ThemeId is null || x.Id == request.ThemeId)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (request.ThemeId is not null && selected.Length == 0) throw new KeyNotFoundException("Tema não encontrado.");

        var books = (await bible.GetBooksAsync(version.Code, ct)).ToDictionary(x => x.BookReferenceId, x => x.Name);
        var sections = new List<ThemeVerseReportSection>();
        foreach (var theme in selected)
        {
            ct.ThrowIfCancellationRequested();
            var items = new List<ThemeVerseReportReference>();
            var links = await references.GetThemeLinksAsync(theme.Id, ct);
            foreach (var link in links.DistinctBy(x => x.ReferenceId))
            {
                ct.ThrowIfCancellationRequested();
                var saved = await references.GetAsync(link.ReferenceId, ct)
                    ?? throw new InvalidOperationException($"Referência vinculada ao tema '{theme.Name}' não encontrada.");
                var passage = await bible.GetPassageAsync(version.Code, saved.BookReferenceId, saved.Chapter, saved.VerseStart, saved.VerseEnd, ct);
                var verseNumbers = passage.Verses.Where(v => !string.IsNullOrWhiteSpace(v.Text)).Select(v => v.Verse).ToHashSet();
                if (Enumerable.Range(saved.VerseStart, saved.VerseEnd - saved.VerseStart + 1).Any(v => !verseNumbers.Contains(v)))
                    throw new InvalidOperationException($"A versão {version.Code} não contém o texto completo da referência {saved.Id}. O relatório não foi gerado.");
                var book = books.GetValueOrDefault(saved.BookReferenceId, $"Livro {saved.BookReferenceId}");
                items.Add(new(saved.Id,
                    BibleReferenceFormatter.Format(book, saved.Chapter, saved.VerseStart, saved.VerseEnd),
                    string.Join(" ", passage.Verses.Select(x => $"{x.Verse} {x.Text}")), link.Observation,
                    saved.BookReferenceId, saved.Chapter, saved.VerseStart, saved.VerseEnd));
            }
            sections.Add(new(theme, items.OrderBy(x => x.BookReferenceId).ThenBy(x => x.Chapter)
                .ThenBy(x => x.VerseStart).ThenBy(x => x.VerseEnd).ThenBy(x => x.SavedReferenceId).ToArray()));
        }
        return new("Temas e versículos", $"{version.Code} - {version.DisplayName}", clock.UtcNow,
            sections.Count, sections.Sum(x => x.References.Count), sections);
    }
}
