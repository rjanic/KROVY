using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Unifies per-member recipe observations into one exact generation recipe.
/// Divergent manual edits make the set non-regenerable without guessing.
/// </summary>
public static class RoofRafterGenerationRecipeRules
{
    public static bool TryUnify(
        IReadOnlyList<RoofRafterGenerationRecipe> members,
        out RoofRafterGenerationRecipe recipe)
    {
        recipe = default!;
        if (members is null || members.Count == 0)
        {
            return false;
        }

        var first = members[0];
        if (!IsValid(first))
        {
            return false;
        }

        for (var index = 1; index < members.Count; index++)
        {
            var current = members[index];
            if (!IsValid(current) ||
                !SameFinite(current.WidthMm, first.WidthMm) ||
                !SameFinite(current.HeightMm, first.HeightMm) ||
                !SameFinite(current.MaximumSpacingMm, first.MaximumSpacingMm) ||
                !string.Equals(current.Material, first.Material, StringComparison.Ordinal))
            {
                return false;
            }
        }

        recipe = first;
        return true;
    }

    public static bool IsValid(RoofRafterGenerationRecipe recipe) =>
        recipe is not null &&
        IsFinitePositive(recipe.WidthMm) &&
        IsFinitePositive(recipe.HeightMm) &&
        IsFinitePositive(recipe.MaximumSpacingMm) &&
        !string.IsNullOrWhiteSpace(recipe.Material);

    private static bool SameFinite(double left, double right) =>
        IsFinitePositive(left) &&
        IsFinitePositive(right) &&
        Math.Abs(left - right) <=
            SimpleGableRoofGeometryTolerance.LengthTolerance(left, right);

    private static bool IsFinitePositive(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
}
