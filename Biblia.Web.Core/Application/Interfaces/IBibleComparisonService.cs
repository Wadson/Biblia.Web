using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleComparisonService
{
    Task<IReadOnlyList<BibleComparisonItem>> CompareAsync(int bookReferenceId,int chapter,int verseStart,int verseEnd,IReadOnlyList<string> versionCodes,CancellationToken cancellationToken=default);
}
