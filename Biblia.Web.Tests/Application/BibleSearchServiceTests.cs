using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.ValueObjects;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.Application;

public sealed class BibleSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_CombinesVersionsHonorsLimitAndFilters()
    {
        var repository=new Repository();var service=new BibleSearchService(repository);
        var result=await service.SearchAsync(new BibleSearchQuery("amor",["ACF","NVI"],43,3,0,2));
        Assert.Equal(2,result.TotalReturned);Assert.Equal(["ACF","NVI"],result.Items.Select(x=>x.VersionCode));Assert.All(repository.Calls,x=>Assert.Equal((43,3),x.Filter));
    }
    private sealed class Repository:IBibleRepository
    {public List<(string Code,(int?,int?) Filter)> Calls{get;}=[];public Task<IReadOnlyList<BibleVerse>> SearchAsync(string v,string q,int? b=null,int? c=null,int skip=0,int take=100,CancellationToken t=default){Calls.Add((v,(b,c)));return Task.FromResult<IReadOnlyList<BibleVerse>>([new(v,43,"João",3,16,"amor")]);}public Task<IReadOnlyList<BibleBook>> GetBooksAsync(string v,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<int>> GetChaptersAsync(string v,int b,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<BibleVerse>> GetVersesAsync(string v,int b,int c,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<BibleVerse>> GetVerseAsync(string v,BibleReference r,CancellationToken t=default)=>throw new NotSupportedException();public Task<BiblePassage> GetPassageAsync(string v,int b,int c,int s,int e,CancellationToken t=default)=>throw new NotSupportedException();}
}

#pragma warning restore xUnit1051
