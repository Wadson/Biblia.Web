using System.Globalization;
using Biblia.Application.Interfaces;

namespace Biblia.Application.Services;

public sealed class DailyVerseService(IBibleVersionManager versions,IBibleRepository bible,ISettingsService settings):IDailyVerseService
{
    private sealed record Candidate(int BookReferenceId,int Chapter,int Verse,string Category);
    private static readonly Candidate[] Candidates=
    [
        new(19,46,1,"Esperança e encorajamento"),
        new(40,24,44,"Preparação"),
        new(52,4,3,"Santificação"),
        new(44,3,19,"Arrependimento"),
        new(43,3,16,"Salvação")
    ];

    public async Task<DailyVerse?> GetDailyVerseAsync(CancellationToken cancellationToken=default)
    {
        await versions.InitializeCatalogAsync(cancellationToken);
        var available=(await versions.GetVersionsAsync(cancellationToken)).Where(x=>x.IsInstalled&&x.IsEnabled).ToArray();
        var active=await versions.GetActiveVersionAsync(cancellationToken);
        var version=available.FirstOrDefault(x=>x.Code==active?.Code)??available.FirstOrDefault();
        if(version is null)return null;

        var today=DateTime.Today;
        var storedDate=await settings.GetAsync("DailyVerseDate",cancellationToken);
        Candidate candidate;
        if(DateTime.TryParseExact(storedDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)&&date==today&&
           int.TryParse(await settings.GetAsync("DailyVerseBookReferenceId",cancellationToken),out var bookId)&&
           int.TryParse(await settings.GetAsync("DailyVerseChapter",cancellationToken),out var chapter)&&
           int.TryParse(await settings.GetAsync("DailyVerseVerse",cancellationToken),out var verse))
        {
            candidate=new(bookId,chapter,verse,await settings.GetAsync("DailyVerseCategory",cancellationToken)??"Versículo do dia");
        }
        else
        {
            candidate=Candidates[(int)(today.Ticks/TimeSpan.TicksPerDay%Candidates.Length)];
            await settings.SetAsync("DailyVerseDate",today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),cancellationToken);
            await settings.SetAsync("DailyVerseBookReferenceId",candidate.BookReferenceId.ToString(CultureInfo.InvariantCulture),cancellationToken);
            await settings.SetAsync("DailyVerseChapter",candidate.Chapter.ToString(CultureInfo.InvariantCulture),cancellationToken);
            await settings.SetAsync("DailyVerseVerse",candidate.Verse.ToString(CultureInfo.InvariantCulture),cancellationToken);
            await settings.SetAsync("DailyVerseCategory",candidate.Category,cancellationToken);
        }

        var books=await bible.GetBooksAsync(version.Code,cancellationToken);
        var book=books.FirstOrDefault(x=>x.BookReferenceId==candidate.BookReferenceId);
        if(book is null)return null;
        var passage=await bible.GetPassageAsync(version.Code,candidate.BookReferenceId,candidate.Chapter,candidate.Verse,candidate.Verse,cancellationToken);
        var text=passage.Verses.FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(text)?null:new(candidate.Category,candidate.BookReferenceId,book.Name,candidate.Chapter,candidate.Verse,$"{book.Name} {candidate.Chapter}:{candidate.Verse}",text,version.Code,version.DisplayName);
    }
}
