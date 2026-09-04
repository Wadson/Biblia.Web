namespace Biblia.Application.Interfaces;

public interface IBibleFilePicker
{
    Task<string?> PickAsync(CancellationToken cancellationToken=default);
}
