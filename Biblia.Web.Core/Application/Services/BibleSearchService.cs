using System.Diagnostics;
using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;

namespace Biblia.Application.Services;

public sealed class BibleSearchService(IBibleRepository repository):IBibleSearchService
{
    public async Task<BibleSearchResult> SearchAsync(BibleSearchQuery query,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(query);ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        if(query.VersionCodes.Count==0)throw new ArgumentException("Selecione ao menos uma versão.",nameof(query));
        if(query.Take is<1 or>500||query.Skip<0)throw new ArgumentOutOfRangeException(nameof(query));
        var watch=Stopwatch.StartNew();var items=new List<BibleVerse>();
        foreach(var code in query.VersionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();var remaining=query.Take-items.Count;if(remaining<=0)break;
            items.AddRange(await repository.SearchAsync(code,query.Text,query.BookReferenceId,query.Chapter,query.Skip,remaining,token));
        }
        watch.Stop();return new(items,items.Count,watch.Elapsed);
    }
}
