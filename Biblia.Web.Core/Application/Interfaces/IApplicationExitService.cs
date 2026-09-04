namespace Biblia.Application.Interfaces;

public interface IApplicationExitService
{
    bool CanExit { get; }
    Task ExitAsync(CancellationToken cancellationToken = default);
}
