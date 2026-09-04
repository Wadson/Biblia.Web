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
    ILogger<ThemeVerseLinkService> logger) : IThemeVerseLinkService
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
        logger.LogInformation("Lote vinculado ao tema {ThemeId}: {Selected} selecionados, {Created} criados, {Reused} reutilizados, {Links} vínculos e {Existing} existentes", themeId, result.Selected, result.ReferencesCreated, result.ReferencesReused, result.LinksCreated, result.AlreadyLinked);
        return result;
    }

    public async Task UnlinkAsync(long themeId, long referenceId, CancellationToken cancellationToken = default)
    {
        if (await themes.GetAsync(themeId, cancellationToken) is null) throw new KeyNotFoundException("Tema não encontrado.");
        if (await references.GetAsync(referenceId, cancellationToken) is null) throw new KeyNotFoundException("Referência não encontrada.");
        await references.RemoveThemeAsync(referenceId, themeId, cancellationToken);
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
        foreach (var link in links)
        {
            var saved = await references.GetAsync(link.ReferenceId, cancellationToken);
            if (saved is not null) details.Add((link, saved));
        }
        var books = (await bible.GetBooksAsync(version.Code, cancellationToken)).ToDictionary(x => x.BookReferenceId, x => x.Name);
        var verseCache = new Dictionary<(int Book, int Chapter), IReadOnlyList<BibleVerse>>();
        var result = new List<ThemeVerseLinkDisplay>();
        foreach (var item in details.OrderBy(x => x.Reference.BookReferenceId).ThenBy(x => x.Reference.Chapter).ThenBy(x => x.Reference.VerseStart))
        {
            var key = (item.Reference.BookReferenceId, item.Reference.Chapter);
            if (!verseCache.TryGetValue(key, out var chapterVerses))
                verseCache[key] = chapterVerses = await bible.GetVersesAsync(version.Code, key.Item1, key.Item2, cancellationToken);
            var text = string.Join(" ", chapterVerses.Where(x => x.Verse >= item.Reference.VerseStart && x.Verse <= item.Reference.VerseEnd).Select(x => x.Text));
            var bookName = books.GetValueOrDefault(item.Reference.BookReferenceId, $"Livro {item.Reference.BookReferenceId}");
            result.Add(new(item.Reference.Id, themeId, bookName, item.Reference.BookReferenceId, item.Reference.Chapter, item.Reference.VerseStart,
                $"{bookName} {item.Reference.Chapter}:{item.Reference.VerseStart}", text, version.Code, version.DisplayName, item.Link.Observation, item.Link.CreatedAt, item.Link.UpdatedAt));
        }
        return result;
    }

    public async Task UpdateObservationAsync(long themeId, long referenceId, string? observation, CancellationToken cancellationToken = default)
    {
        if (observation?.Trim().Length > 2000) throw new InvalidOperationException("A observação deve ter no máximo 2.000 caracteres.");
        await references.UpdateThemeObservationAsync(referenceId, themeId, observation, cancellationToken);
        logger.LogInformation("Observação do vínculo {ThemeId}/{ReferenceId} atualizada", themeId, referenceId);
    }
}
