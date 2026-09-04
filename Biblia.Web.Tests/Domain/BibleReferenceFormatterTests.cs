using Biblia.Domain.Rules;
using Xunit;

namespace Biblia.Tests.Domain;

public sealed class BibleReferenceFormatterTests
{
    [Fact]
    public void Format_SingleVerse_DoesNotRepeatEnd() =>
        Assert.Equal("Levítico 11:45", BibleReferenceFormatter.Format("Levítico", 11, 45, 45));

    [Fact]
    public void Format_Range_UsesEditorialDash() =>
        Assert.Equal("João 3:16–18", BibleReferenceFormatter.Format("João", 3, 16, 18));
}
