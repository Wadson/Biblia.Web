namespace Biblia.Domain.Entities;

public sealed record NaturePhoto(long Id, string PreviewUrl, string RenderUrl, string Photographer, string PhotographerUrl, string PhotoPageUrl, string? AverageColor, bool IsLocal = false, string? SecondaryColor = null)
{
    public string Attribution => IsLocal ? "Fundo BíbliaTema" : $"Foto por {Photographer} no Pexels";
}

public sealed record VerseCardRequest(string Greeting, string? Theme, string VerseText, string Reference, string VersionCode,
    NaturePhoto Background, double OverlayOpacity, string Format, string Template);

public sealed record VerseCardResult(string FilePath, int Width, int Height, string MimeType);
