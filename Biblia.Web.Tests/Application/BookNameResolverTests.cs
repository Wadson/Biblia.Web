using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Biblia.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biblia.Tests.Application;

public sealed class BookNameResolverTests
{
    [Fact]
    public async Task Resolve_UsesPreferredThenActiveAndLoadsEachVersionOnce()
    {
        var active = Version(1, "ACF", true); var preferred = Version(2, "NVI", true); var unavailable = Version(3, "OFF", false);
        var bible = new FakeBible();
        var resolver = new BookNameResolver(new FakeManager(active, [active, preferred, unavailable]), new FakeCatalog([active, preferred, unavailable]), bible, NullLogger<BookNameResolver>.Instance);
        var now = DateTimeOffset.UtcNow;
        var input = new[]
        {
            Details(new(1,43,3,16,16,null,preferred.Id,now,now)),
            Details(new(2,46,13,4,7,null,unavailable.Id,now,now)),
            Details(new(3,22,1,1,2,null,null,now,now)),
        };
        var result = await resolver.ResolveAsync(input, TestContext.Current.CancellationToken);
        Assert.Equal("João 3:16", result[0].FormattedReference); Assert.Equal("NVI", result[0].EffectiveVersionCode);
        Assert.Equal("1 Coríntios 13:4–7", result[1].FormattedReference); Assert.Equal("ACF", result[1].EffectiveVersionCode);
        Assert.Equal("Cânticos 1:1–2", result[2].FormattedReference);
        Assert.Equal(2, bible.GetBooksCalls);
    }

    [Fact]
    public async Task Resolve_FallsBackToCanonicalCatalogWhenNoVersionIsAvailable()
    {
        var unavailable = Version(1, "OFF", false); var resolver = new BookNameResolver(new FakeManager(null, [unavailable]), new FakeCatalog([unavailable]), new FakeBible(), NullLogger<BookNameResolver>.Instance);
        var now = DateTimeOffset.UtcNow; var result = await resolver.ResolveAsync([Details(new(1,9,1,1,1,null,null,now,now))], TestContext.Current.CancellationToken);
        Assert.Equal("1 Samuel 1:1", result[0].FormattedReference); Assert.Null(result[0].EffectiveVersionCode);
    }

    private static SavedReferenceDetails Details(SavedReference value) => new(value, []);
    private static BibleVersionCatalogEntry Version(long id, string code, bool available) => new(id, code, code, "pt-BR", $"{code}.sqlite", available ? $"{code}.sqlite" : null, 2, null, null, null, available, available, true, null, null, available ? BibleVersionValidationStatus.Compatible : BibleVersionValidationStatus.Incompatible, null);

    private sealed class FakeCatalog(IReadOnlyList<BibleVersionCatalogEntry> items) : IBibleVersionCatalogRepository
    {
        public Task<IReadOnlyList<BibleVersionCatalogEntry>> GetAllAsync(CancellationToken cancellationToken=default)=>Task.FromResult(items);
        public Task<BibleVersionCatalogEntry?> GetByCodeAsync(string code,CancellationToken cancellationToken=default)=>Task.FromResult(items.FirstOrDefault(x=>x.Code==code));
        public Task<BibleVersionCatalogEntry> CreateAsync(BibleVersionCatalogEntry entry,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task UpdateAsync(BibleVersionCatalogEntry entry,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task DeleteAsync(long id,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class FakeManager(BibleVersionCatalogEntry? active,IReadOnlyList<BibleVersionCatalogEntry> items) : IBibleVersionManager
    {
        public Task<BibleVersionCatalogEntry?> GetActiveVersionAsync(CancellationToken cancellationToken=default)=>Task.FromResult(active); public Task<IReadOnlyList<BibleVersionCatalogEntry>> GetVersionsAsync(CancellationToken cancellationToken=default)=>Task.FromResult(items);
        public Task InitializeCatalogAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask; public Task SetActiveVersionAsync(string code,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task SetEnabledAsync(string code,bool enabled,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<string> ResolveDatabasePathAsync(string code,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<BibleValidationResult> ValidateAsync(string code,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class FakeBible : IBibleRepository
    {
        public int GetBooksCalls { get; private set; }
        public Task<IReadOnlyList<BibleBook>> GetBooksAsync(string versionCode,CancellationToken cancellationToken=default){GetBooksCalls++;return Task.FromResult<IReadOnlyList<BibleBook>>([new(9,1,"1 Samuel"),new(22,1,"Cânticos"),new(43,2,"João"),new(46,2,"1 Coríntios")]);}
        public Task<IReadOnlyList<int>> GetChaptersAsync(string versionCode,int bookReferenceId,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<IReadOnlyList<BibleVerse>> GetVersesAsync(string versionCode,int bookReferenceId,int chapter,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<IReadOnlyList<BibleVerse>> GetVerseAsync(string versionCode,BibleReference reference,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<BiblePassage> GetPassageAsync(string versionCode,int bookReferenceId,int chapter,int verseStart,int verseEnd,CancellationToken cancellationToken=default)=>throw new NotSupportedException(); public Task<IReadOnlyList<BibleVerse>> SearchAsync(string versionCode,string text,int? bookReferenceId=null,int? chapter=null,int skip=0,int take=100,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}
