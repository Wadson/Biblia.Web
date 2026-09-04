using Biblia.Domain.Entities;

namespace Biblia.Application.Interfaces;

public interface IBibleVersionImportService
{
    Task<BibleVersionImportResult> ImportAsync(string sourcePath,CancellationToken cancellationToken=default);
    Task RemoveImportedAsync(string code,CancellationToken cancellationToken=default);
}
