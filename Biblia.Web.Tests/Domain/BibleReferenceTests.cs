using Biblia.Domain.Exceptions;
using Biblia.Domain.ValueObjects;
using Xunit;

namespace Biblia.Tests.Domain;

public sealed class BibleReferenceTests
{
    [Fact]
    public void Constructor_WithValidCoordinates_PreservesCanonicalReference()
    {
        var reference = new BibleReference(43, 3, 16);
        Assert.Equal(43, reference.BookReferenceId);
        Assert.Equal(3, reference.Chapter);
        Assert.Equal(16, reference.Verse);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(67, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public void Constructor_WithInvalidCoordinates_Throws(int bookReferenceId, int chapter, int verse)
    {
        Assert.Throws<DomainValidationException>(() => new BibleReference(bookReferenceId, chapter, verse));
    }
}
