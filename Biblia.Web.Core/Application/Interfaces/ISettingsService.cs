namespace Biblia.Application.Interfaces;
public interface ISettingsService { Task<string?> GetAsync(string key,CancellationToken cancellationToken=default); Task SetAsync(string key,string value,CancellationToken cancellationToken=default); }
