using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Rules;
using Biblia.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class BookNameResolver(
    IBibleVersionManager versions,
    IBibleVersionCatalogRepository catalog,
    IBibleRepository bible,
    ILogger<BookNameResolver> logger) : IBookNameResolver
{
    private static readonly string[] CanonicalBooks =
    [
        "Gênesis","Êxodo","Levítico","Números","Deuteronômio","Josué","Juízes","Rute","1 Samuel","2 Samuel","1 Reis","2 Reis","1 Crônicas","2 Crônicas","Esdras","Neemias","Ester","Jó","Salmos","Provérbios","Eclesiastes","Cânticos","Isaías","Jeremias","Lamentações de Jeremias","Ezequiel","Daniel","Oséias","Joel","Amós","Obadias","Jonas","Miquéias","Naum","Habacuque","Sofonias","Ageu","Zacarias","Malaquias","Mateus","Marcos","Lucas","João","Atos","Romanos","1 Coríntios","2 Coríntios","Gálatas","Efésios","Filipenses","Colossenses","1 Tessalonicenses","2 Tessalonicenses","1 Timóteo","2 Timóteo","Tito","Filemom","Hebreus","Tiago","1 Pedro","2 Pedro","1 João","2 João","3 João","Judas","Apocalipse"
    ];

    public async Task<IReadOnlyList<ReferenceDisplay>> ResolveAsync(IReadOnlyList<SavedReferenceDetails> references, CancellationToken cancellationToken = default)
    {
        var allVersions = await catalog.GetAllAsync(cancellationToken);
        var active = await versions.GetActiveVersionAsync(cancellationToken);
        var dictionaries = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ReferenceDisplay>(references.Count);

        foreach (var details in references)
        {
            var preferred = details.Reference.PreferredBibleVersionId is long id ? allVersions.FirstOrDefault(x => x.Id == id) : null;
            var effective = IsAvailable(preferred) ? preferred : IsAvailable(active) ? active : null;
            string? name = null;
            if (effective is not null)
            {
                if (!dictionaries.TryGetValue(effective.Code, out var names))
                {
                    names = (await bible.GetBooksAsync(effective.Code, cancellationToken)).ToDictionary(x => x.BookReferenceId, x => x.Name);
                    dictionaries[effective.Code] = names;
                }
                names.TryGetValue(details.Reference.BookReferenceId, out name);
            }
            name ??= details.Reference.BookReferenceId is >= 1 and <= 66 ? CanonicalBooks[details.Reference.BookReferenceId - 1] : null;
            if (name is null)
            {
                name = $"Livro não disponível (cód. {details.Reference.BookReferenceId})";
                logger.LogWarning("Nome canônico do livro {BookReferenceId} não pôde ser resolvido", details.Reference.BookReferenceId);
            }
            var r = details.Reference;
            result.Add(new(details, name, BibleReferenceFormatter.Format(name, r.Chapter, r.VerseStart, r.VerseEnd), effective?.Code, effective?.DisplayName));
        }
        return result;
    }

    private static bool IsAvailable(BibleVersionCatalogEntry? version) => version is { IsInstalled: true, IsEnabled: true } && version.ValidationStatus != BibleVersionValidationStatus.Incompatible;
}
