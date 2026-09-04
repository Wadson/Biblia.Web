using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface ISavedReferenceService
{
    Task<IReadOnlyList<SavedReferenceDetails>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<SavedReferenceDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default);
    Task<SavedReferenceDetails?> FindCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, CancellationToken cancellationToken = default);
    Task<SavedReferenceDetails> GetOrCreateCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, long? preferredVersionId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default);
    Task<SavedReferenceDetails> SaveAsync(long? id, int bookReferenceId, int chapter, int verseStart, int verseEnd, string? comment, long? preferredVersionId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
