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
    double RoofSlopeDegrees,
    LowerEndCutMode LowerEndCutMode = LowerEndCutMode.Vertical,
    RidgeJoinMode RidgeJoinMode = RidgeJoinMode.Meet,
    double StructuralWidthMm = RoofStructuralPhysicalSettings.DefaultWidthMm,
    RoofStructuralHeightMode StructuralHeightMode = RoofStructuralHeightMode.Automatic,
    double StructuralExplicitHeightMm = 0d)
{
    public RoofRafterPreferences ToPreferences() =>
        new(WidthMm, HeightMm, MaximumSpacingMm, Material);
}
