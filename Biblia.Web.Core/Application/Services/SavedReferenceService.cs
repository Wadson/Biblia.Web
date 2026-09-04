using Biblia.Application.Interfaces;
using Biblia.Application.Interfaces.Repositories;
using Biblia.Domain.Entities;
using Biblia.Domain.ValueObjects;

namespace Biblia.Application.Services;

public sealed class SavedReferenceService(ISavedReferenceRepository repository) : ISavedReferenceService
{
    public Task<IReadOnlyList<SavedReferenceDetails>> SearchAsync(string? query, CancellationToken cancellationToken = default) => repository.SearchAsync(query, cancellationToken);
    public Task<SavedReferenceDetails?> GetDetailsAsync(long id, CancellationToken cancellationToken = default) => repository.GetDetailsAsync(id, cancellationToken);
    public async Task<SavedReferenceDetails?> FindCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, CancellationToken cancellationToken = default)
    {
        var range = new BibleReferenceRange(bookReferenceId, chapter, verseStart, verseEnd);
        var item = await repository.FindCanonicalAsync(range.BookReferenceId, range.Chapter, range.VerseStart, range.VerseEnd, cancellationToken);
        return item is null ? null : await repository.GetDetailsAsync(item.Id, cancellationToken);
    }

    public async Task<SavedReferenceDetails> GetOrCreateCanonicalAsync(int bookReferenceId, int chapter, int verseStart, int verseEnd, long? preferredVersionId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default)
    {
        var range = new BibleReferenceRange(bookReferenceId, chapter, verseStart, verseEnd);
        var existing = await repository.FindCanonicalAsync(range.BookReferenceId, range.Chapter, range.VerseStart, range.VerseEnd, cancellationToken);
        if (existing is null)
            return await SaveAsync(null, range.BookReferenceId, range.Chapter, range.VerseStart, range.VerseEnd, null, preferredVersionId, themeIds, cancellationToken);

        var details = (await repository.GetDetailsAsync(existing.Id, cancellationToken))!;
        foreach (var themeId in themeIds.Distinct().Except(details.Themes.Select(x => x.Id)))
            await repository.AddThemeAsync(existing.Id, themeId, cancellationToken);
        return (await repository.GetDetailsAsync(existing.Id, cancellationToken))!;
    }

    public async Task<SavedReferenceDetails> SaveAsync(long? id, int bookReferenceId, int chapter, int verseStart, int verseEnd, string? comment, long? preferredVersionId, IReadOnlyCollection<long> themeIds, CancellationToken cancellationToken = default)
    {
        var range = new BibleReferenceRange(bookReferenceId, chapter, verseStart, verseEnd);
        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        SavedReference item;
        if (id is null or <= 0)
            item = await repository.CreateAsync(new SavedReference(0, range.BookReferenceId, range.Chapter, range.VerseStart, range.VerseEnd, normalizedComment, preferredVersionId, default, default), cancellationToken);
        else
        {
            var existing = await repository.GetAsync(id.Value, cancellationToken) ?? throw new KeyNotFoundException("Referência não encontrada.");
            item = existing with { BookReferenceId = range.BookReferenceId, Chapter = range.Chapter, VerseStart = range.VerseStart, VerseEnd = range.VerseEnd, Comment = normalizedComment, PreferredBibleVersionId = preferredVersionId };
            await repository.UpdateAsync(item, cancellationToken);
        }
        await repository.SetThemesAsync(item.Id, themeIds.Distinct().ToArray(), cancellationToken);
        return (await repository.GetDetailsAsync(item.Id, cancellationToken))!;
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => repository.DeleteAsync(id, cancellationToken);
}
