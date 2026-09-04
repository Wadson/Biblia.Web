using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBookNameResolver
{
    Task<IReadOnlyList<ReferenceDisplay>> ResolveAsync(
        IReadOnlyList<SavedReferenceDetails> references,
        CancellationToken cancellationToken = default);
}
