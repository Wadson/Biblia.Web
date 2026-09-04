using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.Rules;

namespace Biblia.Application.Services;

public sealed class ThemeService(IThemeRepository repository) : IThemeService
{
    public async Task<IReadOnlyList<Theme>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(query)
            ? await repository.GetAllAsync(cancellationToken)
            : await repository.SearchAsync(query, cancellationToken);
    }

    public async Task<ThemeDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default)
    {
        var theme = await repository.GetAsync(id, cancellationToken);
        if (theme is null) return null;
        var usage = await repository.GetUsageAsync(id, cancellationToken);
        return new ThemeDetails(theme, usage.ReferencesCount);
    }

    public async Task<Theme> SaveAsync(long? id, string? name, string? colorHex, string? description, CancellationToken cancellationToken = default)
    {
        var normalized = ThemeRules.Normalize(name, colorHex, description);
        if (id is null or <= 0)
            return await repository.CreateAsync(normalized.Name, normalized.ColorHex, normalized.Description, cancellationToken);

        var current = await repository.GetAsync(id.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Tema não encontrado.");
        var updated = current with { Name = normalized.Name, ColorHex = normalized.ColorHex, Description = normalized.Description };
        await repository.UpdateAsync(updated, cancellationToken);
        return (await repository.GetAsync(updated.Id, cancellationToken))!;
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => repository.DeleteAsync(id, cancellationToken);
}
