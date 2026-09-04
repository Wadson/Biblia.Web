using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;

namespace Biblia.Infrastructure.Repositories;

public sealed class SettingsRepository : SqliteRepositoryBase,ISettingsRepository
{
    private readonly IClock _clock;
    public SettingsRepository(Biblia.Infrastructure.AppDatabase.AppDatabase database,IClock clock):base(database)=>_clock=clock;
    public async Task<Setting?> GetAsync(string key,CancellationToken token=default){await using var c=await Database.OpenConnectionAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT Key,Value,UpdatedAt FROM Setting WHERE Key=$key;";cmd.Parameters.AddWithValue("$key",key);await using var r=await cmd.ExecuteReaderAsync(token);return await r.ReadAsync(token)?new(r.GetString(0),r.GetString(1),ReadDate(r,2)):null;}
    public async Task SetAsync(string key,string value,CancellationToken token=default){ArgumentException.ThrowIfNullOrWhiteSpace(key);await using var c=await Database.OpenConnectionAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO Setting(Key,Value,UpdatedAt) VALUES($key,$value,$updated) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value,UpdatedAt=excluded.UpdatedAt;";cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$value",value);cmd.Parameters.AddWithValue("$updated",_clock.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync(token);}
    public async Task DeleteAsync(string key,CancellationToken token=default){await using var c=await Database.OpenConnectionAsync(token);await using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM Setting WHERE Key=$key;";cmd.Parameters.AddWithValue("$key",key);await cmd.ExecuteNonQueryAsync(token);}
}
