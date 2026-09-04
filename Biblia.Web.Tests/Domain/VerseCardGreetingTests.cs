using Biblia.Domain.Rules;
using Xunit;

namespace Biblia.Tests.Domain;

public sealed class VerseCardGreetingTests
{
    [Theory]
    [InlineData(5,"Bom dia!")]
    [InlineData(11,"Bom dia!")]
    [InlineData(12,"Boa tarde!")]
    [InlineData(17,"Boa tarde!")]
    [InlineData(18,"Boa noite!")]
    [InlineData(4,"Boa noite!")]
    public void Automatic_UsesLocalHour(int hour,string expected) => Assert.Equal(expected,VerseCardGreeting.Automatic(new DateTime(2026,8,20,hour,0,0)));
}
