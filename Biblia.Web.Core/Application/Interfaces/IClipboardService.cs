namespace Biblia.Application.Interfaces;

public interface IClipboardService
{
    Task SetTextAsync(string text,CancellationToken cancellationToken=default);
}
