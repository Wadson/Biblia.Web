namespace Biblia.Domain.Rules;

public static class BibleReferenceFormatter
{
    public static string Format(string bookName, int chapter, int verseStart, int verseEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookName);
        if (chapter < 1 || verseStart < 1 || verseEnd < verseStart)
            throw new ArgumentOutOfRangeException(nameof(verseStart), "Referência bíblica inválida.");
        return verseStart == verseEnd
            ? $"{bookName.Trim()} {chapter}:{verseStart}"
            : $"{bookName.Trim()} {chapter}:{verseStart}–{verseEnd}";
    }
}
