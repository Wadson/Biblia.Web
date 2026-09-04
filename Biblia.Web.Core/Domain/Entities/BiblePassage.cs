namespace Biblia.Domain.Entities;

public sealed record BiblePassage(string VersionCode,int BookReferenceId,int Chapter,int VerseStart,int VerseEnd,IReadOnlyList<BibleVerse> Verses)
{
    public bool HasAmbiguities=>Verses.GroupBy(v=>v.Verse).Any(group=>group.Count()>1);
}
