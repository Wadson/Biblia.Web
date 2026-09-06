using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleValidationService
{
    Task<BibleValidationResult> ValidateAsync(string databasePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ValidateCanonicalBooksAsync(string databasePath, CancellationToken cancellationToken = default);
}
