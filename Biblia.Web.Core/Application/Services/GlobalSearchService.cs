using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;

namespace Biblia.Application.Services;

public sealed class GlobalSearchService(
    IThemeService themes,
    ISavedReferenceService references,
    IBookNameResolver bookNames,
    IBibleVersionManager versions,
    IBibleSearchService bibleSearch) : IGlobalSearchService
{
    public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string? query, bool includeThemes = true, bool includeReferences = true, CancellationToken token = default)
    {
        var result = new List<GlobalSearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        var text = query.Trim();
        if (includeThemes) result.AddRange((await themes.SearchAsync(text, token)).Select(x => new GlobalSearchResult("Tema", x.Id, x.Name, x.Description)));
        if (includeReferences)
        {
            var saved = await references.SearchAsync(null, token);
            var displays = await bookNames.ResolveAsync(saved, token);
            result.AddRange(displays.Where(x => x.FormattedReference.Contains(text, StringComparison.OrdinalIgnoreCase) || (x.Details.Reference.Comment?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)).Select(x => new GlobalSearchResult("Referência", x.Details.Reference.Id, x.FormattedReference, x.Details.Reference.Comment)));
        }

        var activeVersion = await versions.GetActiveVersionAsync(token);
        if (activeVersion is { IsInstalled: true, IsEnabled: true })
        {
            var bible = await bibleSearch.SearchAsync(new BibleSearchQuery(text, [activeVersion.Code], Take: 50), token);
            result.AddRange(bible.Items.Select(match => new GlobalSearchResult("Bíblia", 0, $"{match.BookName} {match.Chapter}:{match.Verse}", match.Text)));
        }
        return result.OrderBy(x => x.Kind).ThenBy(x => x.Title).ToArray();
    }

}
