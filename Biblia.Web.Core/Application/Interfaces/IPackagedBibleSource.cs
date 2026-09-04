namespace Biblia.Application.Interfaces;

public interface IPackagedBibleSource
{
    Task<Stream> OpenReadAsync(string databaseFileName, CancellationToken cancellationToken = default);
}
