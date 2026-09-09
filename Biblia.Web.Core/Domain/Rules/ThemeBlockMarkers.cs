using System.Globalization;
using Biblia.Domain.Entities;

namespace Biblia.Domain.Rules;

/// <summary>Derives markers from the final displayed sequence. No state or persisted prefixes.</summary>
public static class ThemeBlockMarkers
{
    public static IReadOnlyList<MarkedThemeItem<T>> Apply<T>(IEnumerable<T> items, Func<T, ThemeTextBlock?> blockSelector)
    {
        var result = new List<MarkedThemeItem<T>>();
        var number = 0;
        foreach (var item in items)
        {
            var style = blockSelector(item)?.Style.MarkerStyle ?? TextMarkerStyle.None;
            int? index = style is TextMarkerStyle.Numbered or TextMarkerStyle.OrdinalNumbered ? ++number : null;
            var digits = index?.ToString(CultureInfo.InvariantCulture);
            var prefix = style switch
            {
                TextMarkerStyle.Numbered => digits + ".",
                TextMarkerStyle.OrdinalNumbered => digits + "º",
                TextMarkerStyle.Bullet => "•",
                TextMarkerStyle.Dash => "–",
                _ => ""
            };
            result.Add(new(item, index, prefix));
        }
        return result;
    }
}

public sealed record MarkedThemeItem<T>(T Item, int? Number, string Prefix);
