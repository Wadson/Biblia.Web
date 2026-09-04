using Biblia.Application.Interfaces.Repositories;
using Biblia.Application.Services;
using Biblia.Domain.Entities;
using Biblia.Domain.Exceptions;
using Xunit;

namespace Biblia.Tests.Application;

#pragma warning disable xUnit1051

public sealed class ThemeServiceTests
{
    [Fact]
    public async Task SaveSearchDetailsAndDelete_ValidateAndPreserveUsage()
    {
        var repository = new InMemoryThemeRepository();
        var service = new ThemeService(repository);

        var theme = await service.SaveAsync(null, " Graça ", "#336699", "Doutrina central");
        Assert.Equal("Graça", theme.Name);
        Assert.Equal("#336699", theme.ColorHex);
        Assert.Single(await service.SearchAsync("central"));

        repository.Usage = new ThemeUsage(2);
        var details = await service.GetDetailsAsync(theme.Id);
        Assert.Equal(2, details!.ReferencesCount);

        var updated = await service.SaveAsync(theme.Id, "Graça de Deus", "#abcdef", "Revisada");
        Assert.Equal("#ABCDEF", updated.ColorHex);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.SaveAsync(null, "", "#123456", null));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.SaveAsync(null, "Fé", "azul", null));

        await service.DeleteAsync(theme.Id);
        Assert.Empty(await service.SearchAsync(null));
    }

    private sealed class InMemoryThemeRepository : IThemeRepository
    {
        private readonly List<Theme> _items = [];
        public ThemeUsage Usage { get; set; } = new(0);
        public Task<Theme> CreateAsync(string name, string? color, string? description, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var item = new Theme(_items.Count + 1, name, color, description, now, now);
            _items.Add(item); return Task.FromResult(item);
        }
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) { _items.RemoveAll(item => item.Id == id); return Task.CompletedTask; }
        public Task<Theme?> GetAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_items.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<Theme>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Theme>>(_items.OrderBy(item => item.Name).ToArray());
        public Task<ThemeUsage> GetUsageAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(Usage);
        public Task<IReadOnlyList<Theme>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Theme>>(_items.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray());
        public Task UpdateAsync(Theme theme, CancellationToken cancellationToken = default) { var index = _items.FindIndex(item => item.Id == theme.Id); _items[index] = theme; return Task.CompletedTask; }
    }
}

#pragma warning restore xUnit1051
