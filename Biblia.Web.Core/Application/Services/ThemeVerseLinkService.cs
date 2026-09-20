using Biblia.Domain.Rules;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class ThemeVerseLinkService(
    IThemeRepository themes,
    IBibleVersionCatalogRepository versions,
    IBibleRepository bible,
    ISavedReferenceRepository references,
    ILogger<ThemeVerseLinkService> logger,
    IThemeContentService? content=null, IPublicationService? publications=null) : IThemeVerseLinkService
{
    public async Task<LinkVersesToThemeResult> LinkAsync(long themeId, string versionCode, IReadOnlyCollection<VerseSelection> selections, bool replaceExistingPreferredVersion, CancellationToken cancellationToken = default)
    {
        if (await themes.GetAsync(themeId, cancellationToken) is null) throw new KeyNotFoundException("Tema não encontrado.");
        var version = await versions.GetByCodeAsync(versionCode, cancellationToken) ?? throw new KeyNotFoundException("Versão bíblica não encontrada.");
        if (!version.IsInstalled || !version.IsEnabled || version.ValidationStatus == BibleVersionValidationStatus.Incompatible)
            throw new InvalidOperationException("A versão selecionada não está instalada, habilitada e compatível.");
        if (selections.Count is < 1 or > 100) throw new InvalidOperationException("Selecione entre 1 e 100 referências por operação.");

        var normalized = selections.Select(x =>
        {
            var range = new BibleReferenceRange(x.BookReferenceId, x.Chapter, x.VerseStart, x.VerseEnd);
            var observation = string.IsNullOrWhiteSpace(x.Observation) ? null : x.Observation.Trim();
            if (observation?.Length > 2000) throw new InvalidOperationException("A observação deve ter no máximo 2.000 caracteres.");
            return new VerseSelection(range.BookReferenceId, range.Chapter, range.VerseStart, range.VerseEnd, observation);
        }).Distinct().ToArray();

        foreach (var item in normalized)
        {
            var passage = await bible.GetPassageAsync(version.Code, item.BookReferenceId, item.Chapter, item.VerseStart, item.VerseEnd, cancellationToken);
            var expected = item.VerseEnd - item.VerseStart + 1;
            if (passage.Verses.Select(x => x.Verse).Distinct().Count() != expected || passage.HasAmbiguities)
                throw new InvalidOperationException($"Referência inválida ou ambígua: livro {item.BookReferenceId}, capítulo {item.Chapter}, versículos {item.VerseStart}-{item.VerseEnd}.");
        }

        var result = await references.LinkBatchToThemeAsync(themeId, version.Id, normalized, replaceExistingPreferredVersion, cancellationToken);
        if(content is not null)await content.GetAsync(themeId,cancellationToken);
        logger.LogInformation("Lote vinculado ao tema {ThemeId}: {Selected} selecionados, {Created} criados, {Reused} reutilizados, {Links} vínculos e {Existing} existentes", themeId, result.Selected, result.ReferencesCreated, result.ReferencesReused, result.LinksCreated, result.AlreadyLinked);
        return result;
    }
    public async Task<LinkVersesToThemeResult> LinkToPublicationAsync(long publicationId,long themeId,string versionCode,IReadOnlyCollection<VerseSelection> selections,bool replaceExistingPreferredVersion,CancellationToken cancellationToken=default)
    {
        if(publicationId<=0 || publications is null || await publications.GetAsync(publicationId,cancellationToken) is null) throw new InvalidOperationException("Selecione uma publicação válida.");
        var result=await LinkAsync(themeId,versionCode,selections,replaceExistingPreferredVersion,cancellationToken);
        foreach(var s in selections){var saved=await references.FindCanonicalAsync(s.BookReferenceId,s.Chapter,s.VerseStart,s.VerseEnd,cancellationToken);if(saved is not null)await publications.AddReferenceAsync(publicationId,themeId,saved.Id,cancellationToken);}
        await publications.ApplyAutomaticOrderingAsync(publicationId,themeId,cancellationToken);
        return result;
    }
    public Task UnlinkFromPublicationAsync(long publicationId,long themeId,long referenceId,CancellationToken cancellationToken=default)=>publications is null?throw new InvalidOperationException("Serviço de publicação indisponível."):publications.RemoveReferenceAsync(publicationId,themeId,referenceId,cancellationToken);

    public async Task UnlinkAsync(long themeId, long referenceId, CancellationToken cancellationToken = default)
    {
        if (await themes.GetAsync(themeId, cancellationToken) is null) throw new KeyNotFoundException("Tema não encontrado.");
        if (await references.GetAsync(referenceId, cancellationToken) is null) throw new KeyNotFoundException("Referência não encontrada.");
        await references.RemoveThemeAsync(referenceId, themeId, cancellationToken);
        if(content is not null)await content.GetAsync(themeId,cancellationToken);
        logger.LogInformation("Referência {ReferenceId} desvinculada do tema {ThemeId}; referência preservada", referenceId, themeId);
    }

    public async Task<IReadOnlyList<ThemeVerseLinkDisplay>> GetLinkedAsync(long themeId, string versionCode, CancellationToken cancellationToken = default)
    {
        if (await themes.GetAsync(themeId, cancellationToken) is null) throw new KeyNotFoundException("Tema não encontrado.");
        var version = await versions.GetByCodeAsync(versionCode, cancellationToken) ?? throw new KeyNotFoundException("Versão bíblica não encontrada.");
        if (!version.IsInstalled || !version.IsEnabled || version.ValidationStatus == BibleVersionValidationStatus.Incompatible)
            throw new InvalidOperationException("A versão selecionada não está disponível.");
        var links = await references.GetThemeLinksAsync(themeId, cancellationToken);
        var details = new List<(ReferenceTheme Link, SavedReference Reference)>();
        foreach (var link in links.DistinctBy(x => x.ReferenceId))
        {
            var saved = await references.GetAsync(link.ReferenceId, cancellationToken);
            if (saved is not null) details.Add((link, saved));
        }
        var books = (await bible.GetBooksAsync(version.Code, cancellationToken)).ToDictionary(x => x.BookReferenceId, x => x.Name);
        var verseCache = new Dictionary<(int Book, int Chapter), IReadOnlyList<BibleVerse>>();
        var result = new List<ThemeVerseLinkDisplay>();
        foreach (var item in details.OrderBy(x => BibleCanonicalOrder.Key(x.Reference)))
        {
            var key = (item.Reference.BookReferenceId, item.Reference.Chapter);
            if (!verseCache.TryGetValue(key, out var chapterVerses))
                verseCache[key] = chapterVerses = await bible.GetVersesAsync(version.Code, key.Item1, key.Item2, cancellationToken);
            var text = string.Join(" ", chapterVerses.Where(x => x.Verse >= item.Reference.VerseStart && x.Verse <= item.Reference.VerseEnd).Select(x => x.Text));
            var bookName = books.GetValueOrDefault(item.Reference.BookReferenceId, $"Livro {item.Reference.BookReferenceId}");
            result.Add(new(item.Reference.Id, themeId, bookName, item.Reference.BookReferenceId, item.Reference.Chapter, item.Reference.VerseStart,
                BibleReferenceFormatter.Format(bookName, item.Reference.Chapter, item.Reference.VerseStart, item.Reference.VerseEnd), text, version.Code, version.DisplayName, item.Link.Observation, item.Link.CreatedAt, item.Link.UpdatedAt, item.Reference.VerseEnd));
        }
        return result;
    }

    public async Task UpdateObservationAsync(long themeId, long referenceId, string? observation, CancellationToken cancellationToken = default)
    {
        if (observation?.Trim().Length > 2000) throw new InvalidOperationException("A observação deve ter no máximo 2.000 caracteres.");
        await references.UpdateThemeObservationAsync(referenceId, themeId, observation, cancellationToken);
        logger.LogInformation("Observação do vínculo {ThemeId}/{ReferenceId} atualizada", themeId, referenceId);
    }

    public async Task SanitizePublicationObservationsAsync(long publicationId, long themeId, CancellationToken cancellationToken = default)
    {
        if (publications is null || publicationId <= 0 || themeId <= 0) return;

        var blockContents = (await publications.GetContentAsync(publicationId, themeId, cancellationToken))
            .Where(item => item.TextBlock is not null)
            .Select(item => NormalizeBlockText(item.TextBlock!.Content))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (blockContents.Count == 0) return;

        foreach (var link in await references.GetThemeLinksAsync(themeId, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(link.Observation)) continue;
            if (!blockContents.Contains(NormalizeBlockText(link.Observation))) continue;
            await references.UpdateThemeObservationAsync(link.ReferenceId, themeId, null, cancellationToken);
            logger.LogWarning("Observação contaminada por bloco removida do vínculo {ThemeId}/{ReferenceId}", themeId, link.ReferenceId);
        }
    }

    private static string NormalizeBlockText(string value)
    {
        var normalized = string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return System.Text.RegularExpressions.Regex.Replace(normalized, @"^(?:\d+[ºo.]\s*)+", string.Empty, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}
