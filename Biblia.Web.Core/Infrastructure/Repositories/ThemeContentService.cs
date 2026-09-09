using System.Text.Json;
using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Domain.Rules;
using Microsoft.Data.Sqlite;

namespace Biblia.Infrastructure.Repositories;

// One transaction owns every mixed-sequence operation, including canonical slot replacement.
public sealed class ThemeContentService(Biblia.Infrastructure.AppDatabase.AppDatabase database) : IThemeContentService
{
    public async Task<ThemeContentSequence> GetAsync(long themeId,CancellationToken ct=default)
        => await Execute(themeId, null, ct);
    public async Task SaveBlockAsync(long themeId,long? itemId,ThemeTextBlock block,CancellationToken ct=default)
    {
        block=block.Validate();
        await Execute(themeId,async(c,tx,mode,items)=>
        {
            if(itemId is not null && !items.Any(x=>x.Id==itemId&&x.TextBlock is not null)) throw new KeyNotFoundException("Bloco não encontrado neste tema.");
            await using var cmd=c.CreateCommand(); cmd.Transaction=tx;
            cmd.CommandText=itemId is null
                ? "INSERT INTO ThemeContent(ThemeId,SortOrder,BlockJson,CreatedAt,UpdatedAt) VALUES($theme,$pos,$block,$now,$now);"
                : "UPDATE ThemeContent SET BlockJson=$block,UpdatedAt=$now WHERE Id=$id AND ThemeId=$theme AND ReferenceId IS NULL;";
            cmd.Parameters.AddWithValue("$theme",themeId);cmd.Parameters.AddWithValue("$pos",items.Count==0?0:items.Max(x=>x.SortOrder)+1);
            cmd.Parameters.AddWithValue("$id",itemId??0);cmd.Parameters.AddWithValue("$block",JsonSerializer.Serialize(block));cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        },ct);
    }
    public async Task DeleteBlockAsync(long themeId,long itemId,CancellationToken ct=default)
        => await Execute(themeId,async(c,tx,mode,items)=>
        {
            if(!items.Any(x=>x.Id==itemId&&x.TextBlock is not null))throw new KeyNotFoundException("Bloco não encontrado.");
            await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="DELETE FROM ThemeContent WHERE Id=$id AND ThemeId=$theme AND ReferenceId IS NULL;";
            cmd.Parameters.AddWithValue("$id",itemId);cmd.Parameters.AddWithValue("$theme",themeId);await cmd.ExecuteNonQueryAsync(ct);
        },ct);
    public async Task MoveAsync(long themeId,long itemId,int direction,CancellationToken ct=default)
        => await Execute(themeId,async(c,tx,mode,items)=>
        {
            if(mode!=ThemeOrderingMode.Manual)throw new InvalidOperationException("Ative a ordenação manual para mover itens.");
            if(direction is not (-1 or 1))throw new ArgumentException("Direção inválida.");
            var i=items.FindIndex(x=>x.Id==itemId);if(i<0)throw new KeyNotFoundException("Item não encontrado.");
            var next=i+direction;if(next<0||next>=items.Count)throw new InvalidOperationException("O item já está no limite da lista.");
            (items[i],items[next])=(items[next],items[i]);await Persist(c,tx,themeId,items,ct);
        },ct);
    public async Task SetModeAsync(long themeId,ThemeOrderingMode mode,bool confirmCanonical=false,CancellationToken ct=default)
        => await Execute(themeId,async(c,tx,current,items)=>
        {
            if(!Enum.IsDefined(mode))throw new ArgumentException("Modo inválido.");
            if(current==ThemeOrderingMode.Manual&&mode==ThemeOrderingMode.Canonical&&!confirmCanonical)
                throw new InvalidOperationException("Confirme a volta à ordenação automática.");
            await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE Theme SET OrderingMode=$mode WHERE Id=$id;";
            cmd.Parameters.AddWithValue("$mode",(int)mode);cmd.Parameters.AddWithValue("$id",themeId);await cmd.ExecuteNonQueryAsync(ct);
        },ct);
    private async Task<ThemeContentSequence> Execute(long themeId,Func<SqliteConnection,SqliteTransaction,ThemeOrderingMode,List<ThemeContentItem>,Task>? action,CancellationToken ct)
    {
        await using var c=await database.OpenConnectionAsync(ct);await using var tx=c.BeginTransaction(deferred:false);
        async Task<ThemeOrderingMode> Mode()
        {
            await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT OrderingMode FROM Theme WHERE Id=$id;";cmd.Parameters.AddWithValue("$id",themeId);
            return (ThemeOrderingMode)Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)??throw new KeyNotFoundException("Tema não encontrado."));
        }
        var mode=await Mode();var items=await Load(c,tx,themeId,mode,ct);
        if(action is not null)await action(c,tx,mode,items);
        mode=await Mode();items=await Load(c,tx,themeId,mode,ct);
        await Persist(c,tx,themeId,items,ct);await tx.CommitAsync(ct);return new(mode,items.Select((x,i)=>x with{SortOrder=i}).ToArray());
    }
    private static async Task<List<ThemeContentItem>> Load(SqliteConnection c,SqliteTransaction tx,long themeId,ThemeOrderingMode mode,CancellationToken ct)
    {
        var items=new List<ThemeContentItem>();var keys=new Dictionary<long,(int,int,int,int,long)>();
        await using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="SELECT i.Id,i.SortOrder,i.ReferenceId,i.BlockJson,r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd FROM ThemeContent i LEFT JOIN SavedReference r ON r.Id=i.ReferenceId WHERE i.ThemeId=$theme ORDER BY i.SortOrder,i.Id;";
            cmd.Parameters.AddWithValue("$theme",themeId);await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))
            {
                var id=r.GetInt64(0);long? reference=r.IsDBNull(2)?null:r.GetInt64(2);
                items.Add(new(id,themeId,r.GetInt32(1),reference,r.IsDBNull(3)?null:JsonSerializer.Deserialize<ThemeTextBlock>(r.GetString(3))!.Validate()));
                if(reference is not null)keys[id]=BibleCanonicalOrder.Key(r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7),reference.Value);
            }
        }
        // Blocks keep their slots; only verse slots are filled in canonical order.
        if(mode==ThemeOrderingMode.Canonical)
        {
            var queue=new Queue<ThemeContentItem>(items.Where(x=>x.ReferenceId is not null).OrderBy(x=>keys[x.Id]));
            for(var i=0;i<items.Count;i++)if(items[i].ReferenceId is not null)items[i]=queue.Dequeue();
        }
        return items;
    }
    private static async Task Persist(SqliteConnection c,SqliteTransaction tx,long themeId,List<ThemeContentItem> items,CancellationToken ct)
    {
        if(items.Select((x,i)=>x.SortOrder==i).All(x=>x))return;
        // Move above the existing range first, avoiding UNIQUE collisions during swaps.
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="UPDATE ThemeContent SET SortOrder=SortOrder+(SELECT COALESCE(MAX(SortOrder),0)+1 FROM ThemeContent WHERE ThemeId=$theme) WHERE ThemeId=$theme;";
        cmd.Parameters.AddWithValue("$theme",themeId);await cmd.ExecuteNonQueryAsync(ct);
        cmd.CommandText="UPDATE ThemeContent SET SortOrder=$pos WHERE Id=$id AND ThemeId=$theme;";
        var pos=cmd.Parameters.Add("$pos",SqliteType.Integer);var id=cmd.Parameters.Add("$id",SqliteType.Integer);
        for(var i=0;i<items.Count;i++){pos.Value=i;id.Value=items[i].Id;await cmd.ExecuteNonQueryAsync(ct);}
    }
}
