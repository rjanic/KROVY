using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Recipe-defining TimberElementData fields for ordinary automatic roof rafters.
/// Generic AK_ASSIGN / AK_EDIT must not diverge these across a generated set;
/// cross-section and material changes go through AK_ROOF_RAFTERS.
/// </summary>
public static class RoofGeneratedRafterRecipeMetadataRules
{
    public static bool MutatesRecipeDefiningFields(
        TimberElementData original,
        TimberElementData proposed)
    {
        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }

        if (proposed is null)
        {
            throw new ArgumentNullException(nameof(proposed));
        }

        return original.ElementType != proposed.ElementType ||
               !SameFinite(original.WidthMm, proposed.WidthMm) ||
               !SameFinite(original.HeightMm, proposed.HeightMm) ||
               !string.Equals(original.Material, proposed.Material, StringComparison.Ordinal);
    }

    private static bool SameFinite(double left, double right) =>
        !double.IsNaN(left) &&
        !double.IsInfinity(left) &&
        !double.IsNaN(right) &&
        !double.IsInfinity(right) &&
        Math.Abs(left - right) <= 1e-9;
}
