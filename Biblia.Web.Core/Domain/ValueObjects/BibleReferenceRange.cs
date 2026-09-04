using Biblia.Domain.Exceptions;

namespace Biblia.Domain.ValueObjects;

public readonly record struct BibleReferenceRange
{
    public BibleReferenceRange(int bookReferenceId, int chapter, int verseStart, int verseEnd)
    {
        if (bookReferenceId is < 1 or > 66) throw new DomainValidationException("O livro deve estar entre 1 e 66.");
        if (chapter < 1) throw new DomainValidationException("O capítulo deve ser maior que zero.");
        if (verseStart < 1) throw new DomainValidationException("O versículo inicial deve ser maior que zero.");
        if (verseEnd < verseStart) throw new DomainValidationException("O versículo final deve ser igual ou posterior ao inicial.");
        BookReferenceId = bookReferenceId; Chapter = chapter; VerseStart = verseStart; VerseEnd = verseEnd;
    }
    public int BookReferenceId { get; }
    public int Chapter { get; }
    public int VerseStart { get; }
    public int VerseEnd { get; }
}
