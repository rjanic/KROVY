namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Exact Stage 6 automatic-rafter generation recipe recovered from an existing
/// roof-owned generated set. Global last-used dialog preferences are not authority.
/// </summary>
public sealed record RoofRafterGenerationRecipe(
    double WidthMm,
    double HeightMm,
    double MaximumSpacingMm,
    string Material);
