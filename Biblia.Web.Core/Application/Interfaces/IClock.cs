namespace Biblia.Application.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
