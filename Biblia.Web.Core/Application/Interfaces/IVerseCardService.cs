using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IVerseCardService
{
    Task<VerseCardResult> RenderAsync(VerseCardRequest request, CancellationToken cancellationToken = default);
}

public interface INatureMediaService
{
    IReadOnlyList<NaturePhoto> GetOfflineBackgrounds();
    Task<IReadOnlyList<NaturePhoto>> SearchAsync(string query, int page = 1, int pageSize = 8, CancellationToken cancellationToken = default);
    Task<string?> GetRenderFileAsync(NaturePhoto photo, CancellationToken cancellationToken = default);
}
