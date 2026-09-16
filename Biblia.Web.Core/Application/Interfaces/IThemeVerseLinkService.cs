using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IThemeVerseLinkService
{
    Task<LinkVersesToThemeResult> LinkAsync(
        long themeId,
        string versionCode,
        IReadOnlyCollection<VerseSelection> selections,
        bool replaceExistingPreferredVersion,
        CancellationToken cancellationToken = default);

    Task UnlinkAsync(long themeId, long referenceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ThemeVerseLinkDisplay>> GetLinkedAsync(long themeId, string versionCode, CancellationToken cancellationToken = default);
    Task UpdateObservationAsync(long themeId, long referenceId, string? observation, CancellationToken cancellationToken = default);
    Task<LinkVersesToThemeResult> LinkToPublicationAsync(long publicationId,long themeId,string versionCode,IReadOnlyCollection<VerseSelection> selections,bool replaceExistingPreferredVersion,CancellationToken cancellationToken=default);
    Task UnlinkFromPublicationAsync(long publicationId,long themeId,long referenceId,CancellationToken cancellationToken=default);
}
