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
        var versionCodes=query.VersionCodes.Where(code=>!string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(versionCodes.Length==0)throw new ArgumentException("Selecione ao menos uma versão.",nameof(query));
        var watch=Stopwatch.StartNew();var items=new List<BibleVerse>();
        var minimumPerVersion=query.Take/versionCodes.Length;
        var remainder=query.Take%versionCodes.Length;
        for(var index=0;index<versionCodes.Length;index++)
        {
            token.ThrowIfCancellationRequested();
            var takeForVersion=minimumPerVersion+(index<remainder?1:0);
            if(takeForVersion==0)continue;
            items.AddRange(await repository.SearchAsync(versionCodes[index],query.Text,query.BookReferenceId,query.Chapter,query.Skip,takeForVersion,token));
        }
        watch.Stop();return new(items,items.Count,watch.Elapsed);
    }
}
