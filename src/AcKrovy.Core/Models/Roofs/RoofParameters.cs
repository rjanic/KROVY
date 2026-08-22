namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Optional neutral inputs reserved for later roof solving. S1 deliberately
/// leaves them unspecified and does not infer values from the footprint.
/// </summary>
public sealed record RoofParameters(
    double? SlopeDegrees = null,
    RoofDirection2D? RidgeDirection = null,
    double? OverhangMm = null,
    double? RafterSpacingMm = null,
    double? RafterWidthMm = null,
    double? RafterHeightMm = null,
    double? Face1SlopeDegrees = null,
    double? EaveHeightDifferenceMm = null)
{
    public static RoofParameters Unspecified { get; } = new();
}
