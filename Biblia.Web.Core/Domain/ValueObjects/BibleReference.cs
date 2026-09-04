using Biblia.Domain.Exceptions;

namespace Biblia.Domain.ValueObjects;

public readonly record struct BibleReference
{
    public BibleReference(int bookReferenceId, int chapter, int verse)
    {
        if (bookReferenceId is < 1 or > 66)
            throw new DomainValidationException("A referência do livro deve estar entre 1 e 66.");
        if (chapter < 1)
            throw new DomainValidationException("O capítulo deve ser maior que zero.");
        if (verse < 1)
            throw new DomainValidationException("O versículo deve ser maior que zero.");

        BookReferenceId = bookReferenceId;
        Chapter = chapter;
        Verse = verse;
    }

    public int BookReferenceId { get; }
    public int Chapter { get; }
    public int Verse { get; }
}
