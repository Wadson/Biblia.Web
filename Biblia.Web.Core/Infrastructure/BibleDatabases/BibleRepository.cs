using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace Biblia.Infrastructure.BibleDatabases;

public sealed class BibleRepository(IBibleVersionManager versions):IBibleRepository
{
    public async Task<IReadOnlyList<BibleBook>> GetBooksAsync(string versionCode,CancellationToken token=default)
    {
        var result=new List<BibleBook>();await using var c=await OpenAsync(versionCode,token);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT book_reference_id,testament_reference_id,name FROM book ORDER BY book_reference_id;";await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))result.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2)));return result;
    }
    public async Task<IReadOnlyList<int>> GetChaptersAsync(string versionCode,int bookReferenceId,CancellationToken token=default)
    {
        var result=new List<int>();await using var c=await OpenAsync(versionCode,token);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DISTINCT v.chapter FROM verse v JOIN book b ON b.id=v.book_id WHERE b.book_reference_id=$book ORDER BY v.chapter;";cmd.Parameters.AddWithValue("$book",bookReferenceId);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))result.Add(r.GetInt32(0));return result;
    }
    public async Task<IReadOnlyList<BibleVerse>> GetVersesAsync(string versionCode,int bookReferenceId,int chapter,CancellationToken token=default)=>await QueryVersesAsync(versionCode,bookReferenceId,chapter,null,null,token);
    public Task<IReadOnlyList<BibleVerse>> GetVerseAsync(string versionCode,BibleReference reference,CancellationToken token=default)=>QueryVersesAsync(versionCode,reference.BookReferenceId,reference.Chapter,reference.Verse,reference.Verse,token);
    public async Task<BiblePassage> GetPassageAsync(string versionCode,int bookReferenceId,int chapter,int verseStart,int verseEnd,CancellationToken token=default)
    {
        if(bookReferenceId is<1 or>66||chapter<1||verseStart<1||verseEnd<verseStart)throw new ArgumentOutOfRangeException(nameof(verseStart),"Intervalo bíblico inválido.");
        var verses=await QueryVersesAsync(versionCode,bookReferenceId,chapter,verseStart,verseEnd,token);return new(versionCode.ToUpperInvariant(),bookReferenceId,chapter,verseStart,verseEnd,verses);
    }
    public async Task<IReadOnlyList<BibleVerse>> SearchAsync(string versionCode,string text,int? bookReferenceId=null,int? chapter=null,int skip=0,int take=100,CancellationToken token=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);if(skip<0||take is<1 or>500)throw new ArgumentOutOfRangeException(nameof(take));
        var result=new List<BibleVerse>();await using var c=await OpenAsync(versionCode,token);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT b.book_reference_id,b.name,v.chapter,v.verse,v.text FROM verse v JOIN book b ON b.id=v.book_id WHERE v.text LIKE $pattern ESCAPE '\\' AND ($book IS NULL OR b.book_reference_id=$book) AND ($chapter IS NULL OR v.chapter=$chapter) ORDER BY b.book_reference_id,v.chapter,v.verse,v.id LIMIT $take OFFSET $skip;";
        var escaped=text.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");cmd.Parameters.AddWithValue("$pattern","%"+escaped+"%");cmd.Parameters.AddWithValue("$book",bookReferenceId is null?DBNull.Value:bookReferenceId.Value);cmd.Parameters.AddWithValue("$chapter",chapter is null?DBNull.Value:chapter.Value);cmd.Parameters.AddWithValue("$take",take);cmd.Parameters.AddWithValue("$skip",skip);
        await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))result.Add(new(versionCode.ToUpperInvariant(),r.GetInt32(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4)));return result;
    }
    private async Task<IReadOnlyList<BibleVerse>> QueryVersesAsync(string versionCode,int bookReferenceId,int chapter,int? start,int? end,CancellationToken token)
    {
        var result=new List<BibleVerse>();await using var c=await OpenAsync(versionCode,token);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT b.book_reference_id,b.name,v.chapter,v.verse,v.text FROM verse v JOIN book b ON b.id=v.book_id WHERE b.book_reference_id=$book AND v.chapter=$chapter AND ($start IS NULL OR v.verse BETWEEN $start AND $end) ORDER BY v.verse,v.id;";cmd.Parameters.AddWithValue("$book",bookReferenceId);cmd.Parameters.AddWithValue("$chapter",chapter);cmd.Parameters.AddWithValue("$start",start is null?DBNull.Value:start.Value);cmd.Parameters.AddWithValue("$end",end is null?DBNull.Value:end.Value);await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token))result.Add(new(versionCode.ToUpperInvariant(),r.GetInt32(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4)));return result;
    }
    private async Task<SqliteConnection> OpenAsync(string code,CancellationToken token)
    {
        var path=await versions.ResolveDatabasePathAsync(code,token);var cs=new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString();var c=new SqliteConnection(cs);try{await c.OpenAsync(token);return c;}catch{await c.DisposeAsync();throw;}
    }
}
