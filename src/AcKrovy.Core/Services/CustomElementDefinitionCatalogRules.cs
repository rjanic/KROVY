using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class CustomElementDefinitionCatalogRules
{
    public static IReadOnlyList<CustomElementDefinition> ApplyRename(
        IEnumerable<CustomElementDefinition>? definitions,
        CustomElementDefinition renamed)
    {
        var normalizedRename = CustomElementDefinitionRules.Normalize(renamed);
        return Normalize(
            (definitions ?? [])
            .Where(definition => !string.Equals(
                definition.Id,
                normalizedRename.Id,
                StringComparison.OrdinalIgnoreCase))
            .Append(normalizedRename));
    }

    public static IReadOnlyList<CustomElementDefinition> Normalize(
        IEnumerable<CustomElementDefinition>? definitions)
    {
        var normalized = (definitions ?? [])
            .Select(CustomElementDefinitionRules.Normalize)
            .GroupBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.Prefix, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicatePrefix = normalized
            .GroupBy(definition => definition.Prefix, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);
        if (duplicatePrefix is not null)
        {
            throw new ArgumentException(
                $"Custom element prefix '{duplicatePrefix.Key}' is already in use.",
                nameof(definitions));
        }

        return normalized;
    }
}
