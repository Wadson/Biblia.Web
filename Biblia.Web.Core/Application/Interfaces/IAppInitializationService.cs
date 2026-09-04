namespace Biblia.Application.Interfaces;

public interface IAppInitializationService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
