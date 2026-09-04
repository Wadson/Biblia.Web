using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces.Repositories;

public interface ISavedReferenceRepository
{
    Task<SavedReference> CreateAsync(SavedReference reference, CancellationToken cancellationToken = default);
    Task<SavedReference?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<SavedReferenceDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default);
    Task<SavedReference?> FindCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Busca canônica não implementada pelo repositório.");
    Task<IReadOnlyList<SavedReferenceDetails>> SearchAsync(string? query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedReferenceDetails>> GetByThemeIdsAsync(IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Consulta explícita por temas não implementada pelo repositório.");
    Task UpdateAsync(SavedReference reference, CancellationToken cancellationToken = default);
    Task AddThemeAsync(long referenceId, long themeId, CancellationToken cancellationToken = default);
    Task RemoveThemeAsync(long referenceId, long themeId, CancellationToken cancellationToken = default);
    Task SetThemesAsync(long referenceId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default);
    Task<LinkVersesToThemeResult> LinkBatchToThemeAsync(long themeId, long preferredVersionId, IReadOnlyCollection<VerseSelection> selections, bool replaceExistingPreferredVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferenceTheme>> GetThemeLinksAsync(long themeId, CancellationToken cancellationToken = default);
    Task UpdateThemeObservationAsync(long referenceId, long themeId, string? observation, CancellationToken cancellationToken = default);
    Task<int> CountThemeLinksAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
