using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class CustomElementDefinitionRenameRules
{
    public static CustomElementDefinition Rename(
        CustomElementDefinition definition,
        string newName)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        return CustomElementDefinitionRules.Normalize(
            definition with { Name = newName });
    }

    public static bool HasChanged(
        CustomElementDefinition original,
        CustomElementDefinition renamed)
    {
        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        if (renamed is null)
        {
            throw new ArgumentNullException(nameof(renamed));
        }

        var normalizedOriginal = CustomElementDefinitionRules.Normalize(original);
        var normalizedRenamed = CustomElementDefinitionRules.Normalize(renamed);
        return !string.Equals(
            normalizedOriginal.Name,
            normalizedRenamed.Name,
            StringComparison.Ordinal);
    }

    public static TimberElementData Apply(
        TimberElementData data,
        CustomElementDefinition renamed)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var normalized = CustomElementDefinitionRules.Normalize(renamed);
        if (data.ElementType != TimberElementType.Custom ||
            !string.Equals(
                data.CustomElementTypeId,
                normalized.Id,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                data.CustomElementTypeName,
                normalized.Name,
                StringComparison.Ordinal))
        {
            return data;
        }

        return data with { CustomElementTypeName = normalized.Name };
    }
}
