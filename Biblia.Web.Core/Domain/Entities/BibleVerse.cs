namespace Biblia.Domain.Entities;

public sealed record BibleVerse(string VersionCode,int BookReferenceId,string BookName,int Chapter,int Verse,string Text);
