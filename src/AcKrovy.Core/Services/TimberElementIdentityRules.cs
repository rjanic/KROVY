using System.Text.RegularExpressions;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementIdentityRules
{
    public static string CreateElementId(TimberElementType type, int number)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Číslo prvku musí byť kladné.");
        }

        return $"{TimberElementLabels.Prefix(type)}{number}";
    }

    public static int? TryParseElementNumber(string elementId, TimberElementType type)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var prefix = TimberElementLabels.Prefix(type);
        var match = Regex.Match(
            elementId.Trim(),
            $"^{Regex.Escape(prefix)}(?<number>\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success && int.TryParse(match.Groups["number"].Value, out var number)
            ? number
            : null;
    }
}
