namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Immutable result of the Stage 6 task dialog. Slope is copied from the selected
/// semantic roof and is deliberately absent from remembered preferences.
/// </summary>
public sealed record RoofRafterCreationRequest(
    double WidthMm,
    double HeightMm,
    double MaximumSpacingMm,
    string Material,
    double RoofSlopeDegrees)
{
    public RoofRafterPreferences ToPreferences() =>
        new(WidthMm, HeightMm, MaximumSpacingMm, Material);
}
