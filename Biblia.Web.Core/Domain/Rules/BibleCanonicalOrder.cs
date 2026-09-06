using System.Globalization;
using System.Text;
using Biblia.Domain.Entities;

namespace Biblia.Domain.Rules;

/// <summary>BookReferenceId is the canonical position, never the version-local book.id.
/// Names/aliases are used only to validate identity; ordering is entirely numeric.</summary>
public static class BibleCanonicalOrder
{
    public static IReadOnlyList<BibleBook> Books { get; } = Array.AsReadOnly(new string[]
    {
"Gênesis","Êxodo","Levítico","Números","Deuteronômio","Josué","Juízes","Rute","1 Samuel","2 Samuel","1 Reis","2 Reis","1 Crônicas","2 Crônicas","Esdras","Neemias","Ester","Jó","Salmos","Provérbios","Eclesiastes","Cânticos","Isaías","Jeremias","Lamentações","Ezequiel","Daniel","Oseias","Joel","Amós","Obadias","Jonas","Miqueias","Naum","Habacuque","Sofonias","Ageu","Zacarias","Malaquias","Mateus","Marcos","Lucas","João","Atos","Romanos","1 Coríntios","2 Coríntios","Gálatas","Efésios","Filipenses","Colossenses","1 Tessalonicenses","2 Tessalonicenses","1 Timóteo","2 Timóteo","Tito","Filemom","Hebreus","Tiago","1 Pedro","2 Pedro","1 João","2 João","3 João","Judas","Apocalipse"
    }.Select((name, index) => new BibleBook(index + 1, index < 39 ? 1 : 2, name)).ToArray());

    private static readonly IReadOnlyDictionary<string, int> Names = BuildNames();

    public static int Position(int bookReferenceId) => bookReferenceId is >= 1 and <= 66
        ? bookReferenceId : throw new ArgumentOutOfRangeException(nameof(bookReferenceId), "Livro canônico deve estar entre 1 e 66.");

    public static (int Book, int Chapter, int Start, int End, long Id) Key(int book, int chapter, int start, int end, long id)
        => (Position(book), chapter, start, end, id);
    public static (int Book, int Chapter, int Start, int End, long Id) Key(SavedReference reference)
        => Key(reference.BookReferenceId, reference.Chapter, reference.VerseStart, reference.VerseEnd, reference.Id);
    public static (int Book, int Chapter, int Start, int End, long Id) Key(ThemeVerseReportReference reference)
        => Key(reference.BookReferenceId, reference.Chapter, reference.VerseStart, reference.VerseEnd, reference.SavedReferenceId);
    public static (int Book, int Chapter, int Start, int End, long Id) Key(ThemeVerseLinkDisplay reference)
        => Key(reference.BookReferenceId, reference.Chapter, reference.Verse, reference.VerseEnd, reference.ReferenceId);

    public static int? ResolveName(string? name)
    {
        var normalized = Normalize(name);
        // Unaccented "Jo" can mean Jó or abbreviate João: never guess its identity.
        if (normalized == "JO" && !string.Equals(name?.Trim().Normalize(NormalizationForm.FormC), "Jó", StringComparison.OrdinalIgnoreCase)) return null;
        return Names.TryGetValue(normalized, out var id) ? id : null;
    }

    public static IReadOnlyList<string> ValidateBooks(IEnumerable<BibleBook> books)
    {
        var issues = new List<string>();
        var seen = new HashSet<int>();
        foreach (var book in books)
        {
            var id = book.BookReferenceId;
            if (id is < 1 or > 66) { issues.Add($"ID canônico fora do intervalo: {id}."); continue; }
            if (!seen.Add(id)) issues.Add($"ID canônico duplicado: {id}.");
            if (ResolveName(book.Name) != id) issues.Add($"Identidade canônica inválida: ID {id} deve ser {Books[id - 1].Name}, recebido '{book.Name}'.");
            if (book.TestamentReferenceId != Books[id - 1].TestamentReferenceId) issues.Add($"Testamento inválido para o livro {id}.");
        }
        foreach (var book in Books.Where(b => !seen.Contains(b.BookReferenceId))) issues.Add($"Livro canônico ausente: {book.BookReferenceId} ({book.Name}).");
        return issues;
    }

    private static IReadOnlyDictionary<string, int> BuildNames()
    {
        var names = Books.ToDictionary(b => Normalize(b.Name), b => b.BookReferenceId, StringComparer.Ordinal);
        var abbreviations = new[] { "Gn", "Ex", "Lv", "Nm", "Dt", "Js", "Jz", "Rt", "1Sm", "2Sm", "1Rs", "2Rs", "1Cr", "2Cr", "Ed", "Ne", "Et", "Jó", "Sl", "Pv", "Ec", "Ct", "Is", "Jr", "Lm", "Ez", "Dn", "Os", "Jl", "Am", "Ob", "Jn", "Mq", "Na", "Hc", "Sf", "Ag", "Zc", "Ml", "Mt", "Mc", "Lc", "Jo", "At", "Rm", "1Co", "2Co", "Gl", "Ef", "Fp", "Cl", "1Ts", "2Ts", "1Tm", "2Tm", "Tt", "Fm", "Hb", "Tg", "1Pe", "2Pe", "1Jo", "2Jo", "3Jo", "Jd", "Ap" };
        // Jo is ambiguous with Jó after accent removal; require João in that case.
        for (var i = 0; i < abbreviations.Length; i++)
            if (i != 42) names[Normalize(abbreviations[i])] = i + 1;
        foreach (var alias in new[] { "Cantares", "Cântico dos Cânticos", "Cantares de Salomão" }) names[Normalize(alias)] = 22;
        names[Normalize("Lamentações de Jeremias")] = 25;
        names[Normalize("Atos dos Apóstolos")] = 44;
        return names;
    }

    private static string Normalize(string? name) => string.Concat((name ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToUpperInvariant();
}
