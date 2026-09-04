using Biblia.Application.Interfaces;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.Application;

public sealed class BibleComparisonServiceTests
{
    [Fact]
    public async Task CompareAsync_ReportsAvailableMissingAndAmbiguousWithoutChoosingDuplicate()
    {
        var service=new BibleComparisonService(new Repository(),NullLogger<BibleComparisonService>.Instance);
        var items=await service.CompareAsync(43,3,16,16,["ACF","MISS","DUP"]);
        Assert.Equal([BibleComparisonStatus.Available,BibleComparisonStatus.Missing,BibleComparisonStatus.Ambiguous],items.Select(x=>x.Status));Assert.Equal(2,items[2].Verses.Count);
    }
    private sealed class Repository:IBibleRepository
    {public Task<BiblePassage> GetPassageAsync(string v,int b,int c,int s,int e,CancellationToken t=default){IReadOnlyList<BibleVerse> verses=v switch{"MISS"=>[],"DUP"=>[new(v,b,"João",c,s,"A"),new(v,b,"João",c,s,"B")],_=>[new(v,b,"João",c,s,"A")]};return Task.FromResult(new BiblePassage(v,b,c,s,e,verses));}public Task<IReadOnlyList<BibleBook>> GetBooksAsync(string v,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<int>> GetChaptersAsync(string v,int b,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<BibleVerse>> GetVersesAsync(string v,int b,int c,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<BibleVerse>> GetVerseAsync(string v,BibleReference r,CancellationToken t=default)=>throw new NotSupportedException();public Task<IReadOnlyList<BibleVerse>> SearchAsync(string v,string q,int? b=null,int? c=null,int s=0,int take=100,CancellationToken t=default)=>throw new NotSupportedException();}
}

#pragma warning restore xUnit1051
