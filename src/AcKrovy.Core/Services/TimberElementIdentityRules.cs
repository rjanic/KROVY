using System.Text.RegularExpressions;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementIdentityRules
{
    public static string CreateElementId(TimberElementType type, int number)
        => CreateElementId(TimberElementIdentityPrefixes.GetPrefix(type), number);

    public static string CreateElementId(TimberElementData data, int number)
        => CreateElementId(TimberElementSeriesRules.GetPrefix(data), number);

    public static string CreateElementId(string prefix, int number)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Číslo prvku musí byť kladné.");
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix is required.", nameof(prefix));
        }

        return $"{prefix.Trim().ToUpperInvariant()}{number}";
    }

    public static int? TryParseElementNumber(string elementId, TimberElementType type)
        => TryParseElementNumber(elementId, TimberElementIdentityPrefixes.GetPrefix(type));

    public static int? TryParseElementNumber(string elementId, TimberElementData data)
        => TryParseElementNumber(elementId, TimberElementSeriesRules.GetPrefix(data));

    public static int? TryParseElementNumber(string elementId, string prefix)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var match = Regex.Match(
            elementId.Trim(),
            $"^{Regex.Escape(prefix)}(?<number>\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : null;
    }
}
