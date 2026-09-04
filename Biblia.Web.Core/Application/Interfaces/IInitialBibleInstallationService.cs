namespace Biblia.Application.Interfaces;

public interface IInitialBibleInstallationService
{
    Task InstallAsync(CancellationToken cancellationToken = default);
}
