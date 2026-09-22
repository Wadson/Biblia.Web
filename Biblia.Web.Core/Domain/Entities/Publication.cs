using System.Text.RegularExpressions;

namespace Biblia.Domain.Entities;

public sealed record Publication
{
    public long Id {get;set;} public string Name {get;set;} public string? Title {get;set;} public string? Subtitle {get;set;} public string? HeaderText {get;set;} public string? HeaderBackgroundColorHex {get;set;} public string? HeaderTextColorHex {get;set;} public double? TitleFontSize {get;set;} public bool TitleBold {get;set;} public bool TitleItalic {get;set;} public long? BrandingId {get;set;} public DateTimeOffset CreatedAt {get;set;} public DateTimeOffset UpdatedAt {get;set;}
    public string? TitleTextColorHex {get;set;} public double? OrganizationFontSize {get;set;} public string? OrganizationTextColorHex {get;set;} public double? SubtitleFontSize {get;set;} public string? SubtitleTextColorHex {get;set;} public double? HeaderDetailFontSize {get;set;} public string? HeaderDetailTextColorHex {get;set;} public double? LogoWidth {get;set;} public double? LogoHeight {get;set;}
    public Publication(long id,string name,string? title,string? subtitle,string? headerText,string? headerBackgroundColorHex,string? headerTextColorHex,double? titleFontSize,bool titleBold,bool titleItalic,long? brandingId,DateTimeOffset createdAt,DateTimeOffset updatedAt)=> (Id,Name,Title,Subtitle,HeaderText,HeaderBackgroundColorHex,HeaderTextColorHex,TitleFontSize,TitleBold,TitleItalic,BrandingId,CreatedAt,UpdatedAt)=(id,name,title,subtitle,headerText,headerBackgroundColorHex,headerTextColorHex,titleFontSize,titleBold,titleItalic,brandingId,createdAt,updatedAt);
    public static string NormalizeName(string name)
    {
        var value=(name??string.Empty).Trim();
        if(value.Length is < 1 or > 160) throw new ArgumentException("O nome da publicação deve ter entre 1 e 160 caracteres.");
        return value;
    }
    public static string? NormalizeHex(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)) return null;
        value=value.Trim();
        if(!Regex.IsMatch(value,"^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("A cor deve estar no formato #RRGGBB.");
        return value.ToUpperInvariant();
    }
}

public sealed record OrganizationBranding(long Id,string Name,byte[]? Logo,string? LogoContentType,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
/// <summary>Conteúdo exclusivo de uma combinação publicação/tema.</summary>
public sealed record PublicationContentItem(
    long Id, long PublicationId, long ThemeId, int SortOrder, long? ReferenceId,
    ThemeTextBlock? TextBlock, string? Observation = null, long? BibleVersionId = null);
public enum PublicationThemeStatus { None, Partial, Complete }
