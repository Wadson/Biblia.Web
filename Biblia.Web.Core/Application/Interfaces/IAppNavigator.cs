namespace Biblia.Application.Interfaces;

public interface IAppNavigator
{
    Task GoToAsync(string route, IReadOnlyDictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
    Task GoBackAsync(CancellationToken cancellationToken = default);
}
