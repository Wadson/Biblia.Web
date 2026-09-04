namespace Biblia.Domain.Rules;

public static class VerseCardGreeting
{
    public static string Automatic(DateTime localTime) => localTime.Hour switch
    {
        >= 5 and < 12 => "Bom dia!",
        >= 12 and < 18 => "Boa tarde!",
        _ => "Boa noite!"
    };
}
