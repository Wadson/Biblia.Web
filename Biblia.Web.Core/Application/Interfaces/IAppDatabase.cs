namespace Biblia.Application.Interfaces;

public interface IAppDatabase
{
    string DatabasePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default);
    Task ReplaceAsync(string stagedDatabasePath, CancellationToken cancellationToken = default);
}
