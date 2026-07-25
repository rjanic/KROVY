namespace AcKrovy.Cad.Abstractions.Layers;

public static class ElementLayerProfileConflictRules
{
    public static bool TryFindConflict(
        IEnumerable<ElementLayerStyle> styles,
        out string layerName)
    {
        if (styles is null)
        {
            throw new ArgumentNullException(nameof(styles));
        }

        foreach (var group in styles.GroupBy(
                     style => style.LayerName.Trim(),
                     StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            if (group.Any(style =>
                    style.ColorIndex != first.ColorIndex ||
                    !string.Equals(
                        CadLinetypeNames.Normalize(style.LinetypeName),
                        CadLinetypeNames.Normalize(first.LinetypeName),
                        StringComparison.OrdinalIgnoreCase)))
            {
                layerName = group.Key;
                return true;
            }
        }

        layerName = string.Empty;
        return false;
    }
}
