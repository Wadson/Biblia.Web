using System.Reflection;
using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Enums;
using Xunit;

namespace Biblia.Tests.Application;

public sealed class ThemeReportServiceTests
{
    [Fact]
    public async Task All66BooksAreSortedAtApplicationBoundaryDespiteReverseRepositoryOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(1, 66).Reverse().Select(b => new SavedReference(67-b,b,1,1,1,null,null,now,now)).ToArray();
        var themes = Stub<IThemeRepository>((name, _) => name == "GetAsync" ? Task.FromResult<Theme?>(new(1,"Cânon",null!,null,now,now)) : Task.FromResult<IReadOnlyList<Theme>>([new(1,"Cânon",null!,null,now,now)]));
        var entry = new BibleVersionCatalogEntry(1,"ACF","ACF","pt-BR","ACF.sqlite",null,2,null,null,null,true,true,true,now,now,BibleVersionValidationStatus.Compatible,null);
        var versions = Stub<IBibleVersionCatalogRepository>((name, _) => name == "GetAllAsync"
            ? Task.FromResult<IReadOnlyList<BibleVersionCatalogEntry>>([entry]) : Task.FromResult<BibleVersionCatalogEntry?>(entry));
        var references = Stub<ISavedReferenceRepository>((name,args) => name switch
        {
            "GetThemeLinksAsync" => Task.FromResult<IReadOnlyList<ReferenceTheme>>(items.Select(x => new ReferenceTheme(x.Id,1,$"Nota {x.BookReferenceId}",now,now,1)).ToArray()),
            "GetAsync" => Task.FromResult<SavedReference?>(items.Single(x => x.Id == (long)args[0]!)),
            _ => throw new NotSupportedException(name)
        });
        var bible = Stub<IBibleRepository>((name,args) => name switch
        {
            "GetBooksAsync" => Task.FromResult(Biblia.Domain.Rules.BibleCanonicalOrder.Books),
            "GetPassageAsync" => Task.FromResult(new BiblePassage("ACF",(int)args[1]!,1,1,1,[new("ACF",(int)args[1]!,"Livro",1,1,"Texto")])),
            "GetVersesAsync" => Task.FromResult<IReadOnlyList<BibleVerse>>([new("ACF",(int)args[1]!,"Livro",1,1,"Texto")]),
            _ => throw new NotSupportedException(name)
        });
        var report = await new ReportService(references,themes,versions,bible,new FixedClock(now)).BuildThemesAsync(new(1),TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(1,66),report.Sections.Single().References.Select(x=>x.BookReferenceId));
        Assert.Equal(66,report.ReferenceCount);
        var linked = await new ThemeVerseLinkService(themes,versions,bible,references,Microsoft.Extensions.Logging.Abstractions.NullLogger<ThemeVerseLinkService>.Instance)
            .GetLinkedAsync(1,"ACF",TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(1,66),linked.Select(x=>x.BookReferenceId));
        Assert.All(linked,x=>Assert.Equal($"Nota {x.BookReferenceId}",x.Observation));
    }

    [Fact]
    public async Task AllThemesCanonicalOrderSelectedVersionAndLinkObservationArePreserved()
    {
        var report = await Service().BuildThemesAsync(new(null), TestContext.Current.CancellationToken);
        Assert.Equal(2, report.ThemeCount);
        Assert.Equal(2, report.ReferenceCount);
        Assert.Equal(report.ReferenceCount, report.Sections.Sum(s => s.References.Count));
        var section = report.Sections.Single(s => s.Theme.Id == 1);
        Assert.Equal(new long[] { 2, 1 }, section.References.Select(r => r.SavedReferenceId));
        Assert.Equal("Observação do vínculo 2", section.References[0].Observation);
        Assert.DoesNotContain(section.References, r => r.Observation == "Comentário geral");
        Assert.All(section.References, r => Assert.Contains("Texto NVI", r.PassageText));
        Assert.Empty(report.Sections.Single(s => s.Theme.Id == 2).References);
        Assert.All(section.References, r => Assert.Equal("NVI", r.VersionCode));
    }

    [Fact]
    public async Task ThemeFilterIsRespected()
    {
        var report = await Service().BuildThemesAsync(new(2), TestContext.Current.CancellationToken);
        Assert.Single(report.Sections);
        Assert.Equal(2, report.Sections[0].Theme.Id);
        Assert.Equal(0, report.ReferenceCount);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public async Task MissingReferenceOrPassageFailsInsteadOfSilentlyLosingData(bool missingReference, bool missingPassage)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(missingReference, missingPassage)
            .BuildThemesAsync(new(null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PartialPassageFailsInsteadOfExportingAnIncompleteRange()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(incompletePassage: true).BuildThemesAsync(new(null), TestContext.Current.CancellationToken));
    }

    private static ReportService Service(bool missingReference = false, bool missingPassage = false, bool incompletePassage = false)
    {
        var now = DateTimeOffset.UtcNow;
        var themes = Stub<IThemeRepository>((name, args) => Task.FromResult<IReadOnlyList<Theme>>([
            new(1, "Santidade", "#336699", null, now, now), new(2, "Esperança", null!, null, now, now)]));
        var versions = Stub<IBibleVersionCatalogRepository>((name, args) => Task.FromResult<IReadOnlyList<BibleVersionCatalogEntry>>([
            new(1, "NVI", "Nova Versão Internacional", "pt-BR", "nvi.sqlite", "nvi.sqlite", 2, null, null, null,
                true, true, true, now, now, BibleVersionValidationStatus.Compatible, null)]));
        var references = Stub<ISavedReferenceRepository>((name, args) => name switch
        {
            "GetThemeLinksAsync" => Task.FromResult<IReadOnlyList<ReferenceTheme>>((long)args[0]! == 1
                ? [new(1, 1, "Observação do vínculo 1", now, now, 1), new(2, 1, "Observação do vínculo 2", now, now, 1), new(1, 1, "Observação do vínculo 1", now, now, 1)] : []),
            "GetAsync" => Task.FromResult<SavedReference?>(missingReference ? null :
                new((long)args[0]!, (long)args[0]! == 1 ? 43 : 1, 1, 1, incompletePassage ? 2 : 1, "Comentário geral", 999, now, now)),
            _ => throw new NotSupportedException(name)
        });
        var bible = Stub<IBibleRepository>((name, args) => name switch
        {
            "GetBooksAsync" => Task.FromResult<IReadOnlyList<BibleBook>>([new(1, 1, "Gênesis"), new(43, 2, "João")]),
            "GetPassageAsync" => Task.FromResult(new BiblePassage((string)args[0]!, (int)args[1]!, 1, 1, 1,
                missingPassage ? [] : [new((string)args[0]!, (int)args[1]!, "Livro", 1, 1, "Texto " + args[0])])),
            _ => throw new NotSupportedException(name)
        });
        return new(references, themes, versions, bible, new FixedClock(now));
    }

    private static T Stub<T>(Func<string, object?[], object> handler) where T : class
    {
        var instance = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)instance).Handler = handler;
        return instance;
    }
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!.Name, args!);
    }
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}

