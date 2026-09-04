namespace Biblia.Application.Interfaces;

public sealed record DailyVerse(string Category,int BookReferenceId,string BookName,int Chapter,int Verse,string ReferenceText,string BibleText,string BibleVersionCode,string BibleVersionName);

public interface IDailyVerseService
{
    Task<DailyVerse?> GetDailyVerseAsync(CancellationToken cancellationToken=default);
}
