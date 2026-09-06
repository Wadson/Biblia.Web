using Biblia.Domain.Rules;
using Biblia.Domain.Enums;
using Biblia.Infrastructure.BibleDatabases;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biblia.Tests.BibleDataValidation;

public sealed class CanonicalIdentityTests
{
    [Fact]
    public void CatalogHasAllPositionsAndUnambiguousAliases()
    {
        Assert.Equal(Enumerable.Range(1, 66), BibleCanonicalOrder.Books.Select(b => b.BookReferenceId));
        Assert.Empty(BibleCanonicalOrder.ValidateBooks(BibleCanonicalOrder.Books));
        Assert.Equal("Gênesis", BibleCanonicalOrder.Books[0].Name);
        Assert.Equal("Mateus", BibleCanonicalOrder.Books[39].Name);
        Assert.Equal("João", BibleCanonicalOrder.Books[42].Name);
        Assert.Equal("Apocalipse", BibleCanonicalOrder.Books[65].Name);
        Assert.Equal(22, BibleCanonicalOrder.ResolveName("Cantares"));
        Assert.Equal(25, BibleCanonicalOrder.ResolveName("Lamentações de Jeremias"));
        Assert.Equal(28, BibleCanonicalOrder.ResolveName("Oséias"));
        Assert.Equal(62, BibleCanonicalOrder.ResolveName("1 Jo."));
        Assert.Equal(43, BibleCanonicalOrder.ResolveName("JOÃO"));
        Assert.Null(BibleCanonicalOrder.ResolveName("Livro desconhecido"));
        Assert.Null(BibleCanonicalOrder.ResolveName("Jo"));
        Assert.Equal(18, BibleCanonicalOrder.ResolveName("Jó"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BibleCanonicalOrder.Position(67));
    }

    [Theory]
    [InlineData("UPDATE book SET book_reference_id=1 WHERE id=2")]
    [InlineData("DELETE FROM verse WHERE book_id=2; DELETE FROM book WHERE id=2")]
    [InlineData("UPDATE book SET book_reference_id=67 WHERE id=2")]
    [InlineData("UPDATE book SET book_reference_id=NULL WHERE id=2")]
    [InlineData("UPDATE book SET name=CASE id WHEN 1 THEN 'Apocalipse' ELSE 'Gênesis' END WHERE id IN (1,66)")]
    [InlineData("UPDATE book SET testament_reference_id=2 WHERE id=1")]
    [InlineData("UPDATE book SET name=NULL WHERE id=1")]
    public async Task InvalidIdentityIsRejectedEvenWithValidVerseCounts(string mutation)
    {
        var path = Path.Combine(Path.GetTempPath(), $"canonical-{Guid.NewGuid():N}.sqlite");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await BibleValidationServiceTests.CreateBibleAsync(path);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = mutation;
                await command.ExecuteNonQueryAsync(ct);
            }
            var result = await new BibleValidationService(NullLogger<BibleValidationService>.Instance).ValidateAsync(path, ct);
            Assert.Equal(BibleVersionValidationStatus.Incompatible, result.Status);
            Assert.NotEmpty(result.Issues);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
