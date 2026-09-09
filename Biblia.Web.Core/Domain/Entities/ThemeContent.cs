using System.Text.RegularExpressions;

namespace Biblia.Domain.Entities;

public enum ThemeOrderingMode { Canonical, Manual }
public enum TextMarkerStyle { None = 0, Bullet = 1, Numbered = 2, Dash = 3, OrdinalNumbered = 4 }
public sealed record ThemeTextStyle(string TextColorHex = "#172033", string BackgroundColorHex = "#EAF2FF",
    double FontSize = 11, TextMarkerStyle MarkerStyle = TextMarkerStyle.None, bool IsBold = false, bool IsItalic = false)
{
    public void Validate()
    {
        if (!Regex.IsMatch(TextColorHex, "^#[0-9a-fA-F]{6}$") || !Regex.IsMatch(BackgroundColorHex, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Use cores no formato #RRGGBB.");
        if (!double.IsFinite(FontSize) || FontSize is < 7 or > 24 || !Enum.IsDefined(MarkerStyle))
            throw new ArgumentException("Fonte entre 7 e 24 pt e marcador válido são obrigatórios.");
        static double Light(string s)
        {
            double Channel(int n) { var v = Convert.ToInt32(s.Substring(n, 2),16)/255d; return v <= .04045 ? v/12.92 : Math.Pow((v+.055)/1.055,2.4); }
            return .2126*Channel(1)+.7152*Channel(3)+.0722*Channel(5);
        }
        var a=Light(TextColorHex); var b=Light(BackgroundColorHex);
        if ((Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)<4.5) throw new ArgumentException("Contraste insuficiente. Escolha texto escuro com fundo claro ou o inverso (mínimo 4,5:1).");
    }
}
public sealed record ThemeTextBlock(string Content, ThemeTextStyle Style)
{
    public ThemeTextBlock Validate()
    {
        if (string.IsNullOrWhiteSpace(Content) || Content.Trim().Length>10000) throw new ArgumentException("O bloco deve conter entre 1 e 10.000 caracteres.");
        Style.Validate(); return this with { Content=Content.Trim().Replace("\r\n","\n").Replace('\r','\n') };
    }
    public IEnumerable<string> Lines()
    {
        foreach(var line in Content.Split('\n'))
        {
            yield return line;
        }
    }
}
public sealed record ThemeContentItem(long Id, long ThemeId, int SortOrder, long? ReferenceId, ThemeTextBlock? TextBlock);
public sealed record ThemeContentSequence(ThemeOrderingMode Mode, IReadOnlyList<ThemeContentItem> Items);
public sealed record ThemeReportContentItem(ThemeVerseReportReference? Reference, ThemeTextBlock? TextBlock);
