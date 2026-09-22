using Biblia.Domain.Entities;
namespace Biblia.Application.Interfaces;
public interface IPublicationService
{
 Task<IReadOnlyList<Publication>> GetAllAsync(CancellationToken ct=default);
 Task<Publication?> GetAsync(long id,CancellationToken ct=default);
 Task<Publication> SaveAsync(Publication publication,CancellationToken ct=default);
 Task DeleteAsync(long id,CancellationToken ct=default);
 Task<OrganizationBranding?> GetBrandingAsync(CancellationToken ct=default);
 Task SaveBrandingAsync(string name,byte[]? logo,string? contentType,CancellationToken ct=default);
 Task AddReferenceAsync(long publicationId,long themeId,long referenceId,long? bibleVersionId=null,string? observation=null,CancellationToken ct=default);
 Task RemoveReferenceAsync(long publicationId,long themeId,long referenceId,CancellationToken ct=default);
 Task UpdateReferenceObservationAsync(long publicationId,long themeId,long referenceId,string? observation,CancellationToken ct=default);
 Task<IReadOnlyList<PublicationContentItem>> GetContentAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task ApplyAutomaticOrderingAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task<ThemeOrderingMode> GetOrderingModeAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task SetOrderingModeAsync(long publicationId,long themeId,ThemeOrderingMode mode,CancellationToken ct=default);
 /// <summary>Compatibilidade legada: não propaga conteúdo global para uma publicação.</summary>
 Task SynchronizeLegacyThemeContentAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task MoveContentAsync(long publicationId,long themeId,long id,int delta,CancellationToken ct=default);
 Task<long> SaveTextBlockAsync(long publicationId,long themeId,long? id,ThemeTextBlock block,CancellationToken ct=default);
 Task DeleteTextBlockAsync(long publicationId,long themeId,long id,CancellationToken ct=default);
 Task<PublicationThemeStatus> GetThemeStatusAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task LinkCompleteThemeAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task UnlinkThemeAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task LinkThemeAsync(long publicationId,long themeId,CancellationToken ct=default);
 Task<IReadOnlyList<Theme>> GetLinkedThemesAsync(long publicationId,CancellationToken ct=default);
 Task<IReadOnlyList<Publication>> GetLinkedPublicationsAsync(long themeId,CancellationToken ct=default);
}
