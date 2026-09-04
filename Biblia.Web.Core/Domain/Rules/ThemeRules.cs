using System.Text.RegularExpressions;
using Biblia.Domain.Exceptions;

namespace Biblia.Domain.Rules;

public static partial class ThemeRules
{
    public static (string Name, string? ColorHex, string? Description) Normalize(
        string? name,
        string? colorHex,
        string? description)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new DomainValidationException("O nome do tema é obrigatório.");

        var normalizedColor = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim().ToUpperInvariant();
        if (normalizedColor is not null && !HexColor().IsMatch(normalizedColor))
            throw new DomainValidationException("A cor deve estar no formato hexadecimal #RRGGBB.");

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return (normalizedName, normalizedColor, normalizedDescription);
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
