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
    IClock clock,
    IThemeContentService? content = null, IPublicationService? publications = null) : IReportService
{
    public async Task<ReportsOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var items = await references.SearchAsync(null, ct);
        return new((await themes.GetAllAsync(ct)).Count, items.Count,
            await references.CountReferencesWithCommentsAsync(ct), await references.CountThemeLinksAsync(ct));
    }

    public async Task<ThemeVerseReport> BuildThemesAsync(ThemeVerseReportRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Publication? publication=null;
        if(request.PublicationId is long publicationId){if(publications is null)throw new InvalidOperationException("Serviço de publicação indisponível.");publication=await publications.GetAsync(publicationId,ct)??throw new KeyNotFoundException("Publicação não encontrada.");}
        var catalog = (await versions.GetAllAsync(ct)).ToDictionary(v => v.Id);
        var booksByVersion = new Dictionary<long, Dictionary<int, string>>();

        var selected = (await themes.GetAllAsync(ct))
            .Where(x => request.ThemeId is null || x.Id == request.ThemeId)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (request.ThemeId is not null && selected.Length == 0) throw new KeyNotFoundException("Tema não encontrado.");

        var sections = new List<ThemeVerseReportSection>();
        foreach (var theme in selected)
        {
            ct.ThrowIfCancellationRequested();
            var items = new List<ThemeVerseReportReference>();
            var links = await references.GetThemeLinksAsync(theme.Id, ct);
            var allowed=publication is null?null:(await publications!.GetContentAsync(publication.Id,theme.Id,ct)).Where(x=>x.ReferenceId is not null).Select(x=>x.ReferenceId!.Value).ToHashSet();
            foreach (var link in links.DistinctBy(x => x.ReferenceId))
            {
                if(allowed is not null&&!allowed.Contains(link.ReferenceId))continue;
                ct.ThrowIfCancellationRequested();
                var saved = await references.GetAsync(link.ReferenceId, ct)
                    ?? throw new InvalidOperationException($"Referência vinculada ao tema '{theme.Name}' não encontrada.");
                if (link.BibleVersionId is not long versionId || !catalog.TryGetValue(versionId, out var version))
                    throw new InvalidOperationException($"O vínculo da referência {saved.Id} no tema '{theme.Name}' não possui uma versão bíblica registrada. Refaça a vinculação selecionando a versão desejada.");
                if (!version.IsInstalled || !version.IsEnabled || version.ValidationStatus == BibleVersionValidationStatus.Incompatible)
                    throw new InvalidOperationException($"A versão {version.Code} gravada no vínculo da referência {saved.Id} não está disponível. O relatório não foi gerado.");
                if (!booksByVersion.TryGetValue(versionId, out var books))
                {
                    books = (await bible.GetBooksAsync(version.Code, ct)).ToDictionary(x => x.BookReferenceId, x => x.Name);
                    booksByVersion.Add(versionId, books);
                }
                var passage = await bible.GetPassageAsync(version.Code, saved.BookReferenceId, saved.Chapter, saved.VerseStart, saved.VerseEnd, ct);
                var verseNumbers = passage.Verses.Where(v => !string.IsNullOrWhiteSpace(v.Text)).Select(v => v.Verse).ToHashSet();
                if (Enumerable.Range(saved.VerseStart, saved.VerseEnd - saved.VerseStart + 1).Any(v => !verseNumbers.Contains(v)))
                    throw new InvalidOperationException($"A versão {version.Code} não contém o texto completo da referência {saved.Id}. O relatório não foi gerado.");
                var book = books.GetValueOrDefault(saved.BookReferenceId, $"Livro {saved.BookReferenceId}");
                items.Add(new(saved.Id,
                    BibleReferenceFormatter.Format(book, saved.Chapter, saved.VerseStart, saved.VerseEnd),
                    string.Join(" ", passage.Verses.Select(x => $"{x.Verse} {x.Text}")), link.Observation,
                    saved.BookReferenceId, saved.Chapter, saved.VerseStart, saved.VerseEnd, version.Code));
            }
            var ordered = items.OrderBy(x => BibleCanonicalOrder.Key(x)).ToArray();
            // A publication owns its composition. Do not emit a global theme merely because it
            // exists; it must have at least one reference explicitly included in this publication.
            if (publication is not null && items.Count == 0) continue;
            if (content is null) sections.Add(new(theme, ordered));
            else
            {
                var sequence = publication is null ? await content.GetAsync(theme.Id, ct) : null;
                var byId = items.ToDictionary(x => x.SavedReferenceId);
                var source = publication is null ? sequence!.Items.Select(x=>new PublicationContentItem(x.Id,0,theme.Id,x.SortOrder,x.ReferenceId,x.TextBlock)) : await publications!.GetContentAsync(publication.Id,theme.Id,ct);
                var mixed = source.Where(x=>x.ReferenceId is null||byId.ContainsKey(x.ReferenceId.Value)).Select(x => x.ReferenceId is long id
                    ? new ThemeReportContentItem(byId[id], null) : new ThemeReportContentItem(null,x.TextBlock)).ToArray();
                sections.Add(new(theme,mixed.Where(x=>x.Reference is not null).Select(x=>x.Reference!).ToArray(),mixed,publication is null?sequence!.Mode:ThemeOrderingMode.Manual));
            }
        }
        var branding=publications is null?null:await publications.GetBrandingAsync(ct);
        var legacyColor=publication?.HeaderTextColorHex??"#FFFFFF";
        var header=new PdfHeaderOptions(branding?.Name,publication?.Subtitle,publication?.HeaderText,
            publication?.HeaderBackgroundColorHex??"#2458CB", publication?.TitleTextColorHex??legacyColor,
            publication?.OrganizationTextColorHex??legacyColor, publication?.SubtitleTextColorHex??legacyColor,
            publication?.HeaderDetailTextColorHex??legacyColor, publication?.TitleFontSize??23,
            publication?.OrganizationFontSize??11, publication?.SubtitleFontSize??9, publication?.HeaderDetailFontSize??8,
            publication?.TitleBold??true,publication?.TitleItalic??false,branding?.Logo,branding?.LogoContentType,
            publication?.LogoWidth,publication?.LogoHeight);
        return new(request.TitleOverride?.Trim()??publication?.Title??"Temas e versículos", clock.UtcNow,
            sections.Count, sections.Sum(x => x.References.Count), sections,header);
    }
}
