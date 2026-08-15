namespace AcKrovy.Core.Models.Roofs;

/// <summary>User-scoped inputs remembered between automatic-rafter commands.</summary>
public sealed record RoofRafterPreferences(
    double WidthMm,
    double HeightMm,
    double MaximumSpacingMm,
    string Material)
{
    public const double FirstUseWidthMm = 80d;
    public const double FirstUseHeightMm = 160d;
    public const double FirstUseMaximumSpacingMm = 900d;

    public static RoofRafterPreferences CreateFirstUse(string canonicalMaterial) =>
        new(
            FirstUseWidthMm,
            FirstUseHeightMm,
            FirstUseMaximumSpacingMm,
            canonicalMaterial ?? string.Empty);
}
