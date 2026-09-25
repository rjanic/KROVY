using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// WallPlate axis plan-distance from the source eave must be at least half the
/// wall-plate plan width so the outer eave-side face does not extend past the eave.
/// Intermediate and Ridge are not governed by this rule.
/// </summary>
public static class RoofAutomaticPurlinWallPlatePlanDistanceRules
{
    /// <summary>
    /// Minimum plan distance from source eave to the WallPlate axis (mm).
    /// </summary>
    public static double ResolveMinimumPlanDistanceFromEaveMm(double wallPlateWidthMm) =>
        wallPlateWidthMm / 2d;

    /// <summary>
    /// Inclusive minimum: exactly <c>width/2</c> is valid. Uses the shared coordinate
    /// tolerance so floating noise does not reject a true boundary value.
    /// </summary>
    public static bool IsPlanDistanceAtOrAboveMinimum(
        double planDistanceFromEaveMm,
        double wallPlateWidthMm,
        double toleranceMm = RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
    {
        if (!IsFinite(planDistanceFromEaveMm) ||
            !IsFinite(wallPlateWidthMm) ||
            wallPlateWidthMm <= 0d ||
            !IsFinite(toleranceMm) ||
            toleranceMm < 0d)
        {
            return false;
        }

        var minimumMm = ResolveMinimumPlanDistanceFromEaveMm(wallPlateWidthMm);
        return planDistanceFromEaveMm + toleranceMm >= minimumMm;
    }

    /// <summary>
    /// Resolves the WallPlate axis plan distance from the source eave for any
    /// placement mode. PlanDistanceFromEave uses the placement value directly;
    /// PlanDistanceFromRidge and BottomEdge use the resolved roof-surface (axis)
    /// local Z so the displayed ridge/height number is never mistaken for an
    /// eave plan distance.
    /// </summary>
    public static bool TryResolveAxisPlanDistanceFromEaveMm(
        RoofAutomaticPurlinPlacementMode placementMode,
        double placementValueMm,
        double? roofSurfaceLocalZMm,
        double pitchDegrees,
        out double planDistanceFromEaveMm)
    {
        planDistanceFromEaveMm = 0d;
        switch (placementMode)
        {
            case RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave:
                if (!IsFinite(placementValueMm))
                {
                    return false;
                }

                planDistanceFromEaveMm = placementValueMm;
                return true;

            case RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge:
            case RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference:
                // Authoritative physical eave station from the resolved roof-surface Z —
                // never treat a FromRidge / BottomEdge placement number as an eave distance.
                if (roofSurfaceLocalZMm is not { } surfaceZ ||
                    !RoofAutomaticPurlinPitchAdaptationRules.TryResolvePlanDistanceFromEaveMm(
                        surfaceZ,
                        pitchDegrees,
                        out planDistanceFromEaveMm))
                {
                    return false;
                }

                return true;

            default:
                return false;
        }
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
