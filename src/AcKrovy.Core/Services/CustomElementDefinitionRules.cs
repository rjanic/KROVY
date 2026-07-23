using System.Text.RegularExpressions;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class CustomElementDefinitionRules
{
    private static readonly Regex PrefixPattern = new(
        "^[A-Za-z]{1,8}$",
        RegexOptions.CultureInvariant);
    public const int MaximumNameLength = 80;
    public const int MaximumPrefixLength = 8;

    public static CustomElementDefinition Create(string name, string prefix) =>
        Normalize(new CustomElementDefinition(
            Guid.NewGuid().ToString("N"),
            name,
            prefix));

    public static CustomElementDefinition Normalize(CustomElementDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var id = definition.Id?.Trim() ?? string.Empty;
        var name = definition.Name?.Trim() ?? string.Empty;
        var prefix = definition.Prefix?.Trim().ToUpperInvariant() ?? string.Empty;

        if (id.Length == 0)
        {
            throw new ArgumentException("Custom element definition id is required.", nameof(definition));
        }

        if (name.Length == 0 || name.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Custom element name must contain 1 to {MaximumNameLength} characters.",
                nameof(definition));
        }

        if (!PrefixPattern.IsMatch(prefix))
        {
            throw new ArgumentException(
                $"Custom element prefix must contain 1 to {MaximumPrefixLength} ASCII letters.",
                nameof(definition));
        }

        var reservedPrefixes = Enum
            .GetValues(typeof(TimberElementType))
            .Cast<TimberElementType>()
            .Where(type => type != TimberElementType.Custom)
            .Select(TimberElementIdentityPrefixes.GetPrefix);
        if (reservedPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Custom element prefix '{prefix}' is reserved by a built-in element type.",
                nameof(definition));
        }

        return definition with { Id = id, Name = name, Prefix = prefix };
    }

    public static bool TryFromElementData(
        TimberElementData data,
        out CustomElementDefinition? definition)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        definition = null;
        if (data.ElementType != TimberElementType.Custom)
        {
            return false;
        }

        try
        {
            definition = Normalize(new CustomElementDefinition(
                data.CustomElementTypeId ?? string.Empty,
                data.CustomElementTypeName ?? string.Empty,
                data.CustomElementTypePrefix ?? string.Empty));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static TimberElementData Apply(
        TimberElementData data,
        CustomElementDefinition definition)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        var normalized = Normalize(definition);
        return data with
        {
            ElementType = TimberElementType.Custom,
            CustomElementTypeId = normalized.Id,
            CustomElementTypeName = normalized.Name,
            CustomElementTypePrefix = normalized.Prefix,
        };
    }
}
