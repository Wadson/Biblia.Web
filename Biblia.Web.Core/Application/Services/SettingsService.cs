using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
namespace Biblia.Application.Services;
public sealed class SettingsService(ISettingsRepository repository):ISettingsService{public async Task<string?> GetAsync(string key,CancellationToken cancellationToken=default)=>(await repository.GetAsync(key,cancellationToken))?.Value;public Task SetAsync(string key,string value,CancellationToken cancellationToken=default)=>repository.SetAsync(key,value,cancellationToken);}
