namespace Biblia.Domain.Entities;

public sealed record BibleSearchQuery(string Text,IReadOnlyList<string> VersionCodes,int? BookReferenceId=null,int? Chapter=null,int Skip=0,int Take=100);
