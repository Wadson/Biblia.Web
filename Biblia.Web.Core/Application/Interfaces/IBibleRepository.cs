using Biblia.Domain.Entities;
using Biblia.Domain.ValueObjects;

namespace Biblia.Application.Interfaces;

public interface IBibleRepository
{
    Task<IReadOnlyList<BibleBook>> GetBooksAsync(string versionCode,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<int>> GetChaptersAsync(string versionCode,int bookReferenceId,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<BibleVerse>> GetVersesAsync(string versionCode,int bookReferenceId,int chapter,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<BibleVerse>> GetVerseAsync(string versionCode,BibleReference reference,CancellationToken cancellationToken=default);
    Task<BiblePassage> GetPassageAsync(string versionCode,int bookReferenceId,int chapter,int verseStart,int verseEnd,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<BibleVerse>> SearchAsync(string versionCode,string text,int? bookReferenceId=null,int? chapter=null,int skip=0,int take=100,CancellationToken cancellationToken=default);
}
