using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

/// <summary>
/// Resolves the user-facing element type name without translating persistent
/// custom names.
/// </summary>
public static class TimberElementDisplayNameProvider
{
    public static string GetDisplayName(
        TimberElementData data,
        CultureInfo? culture = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        return GetDisplayName(data.ElementType, data.CustomElementTypeName, culture);
    }

    public static string GetDisplayName(
        TimberReportLine line,
        CultureInfo? culture = null)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }
        return GetDisplayName(line.ElementType, line.CustomElementTypeName, culture);
    }

    public static string GetDisplayName(
        TimberElementType type,
        string? customName,
        CultureInfo? culture = null) =>
        type == TimberElementType.Custom && !string.IsNullOrWhiteSpace(customName)
            ? customName!.Trim()
            : TimberElementTypeDisplayNameProvider.GetDisplayName(type, culture);
}
