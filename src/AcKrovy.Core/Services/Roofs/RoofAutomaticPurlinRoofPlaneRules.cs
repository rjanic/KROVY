using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Authoritative technical Strešná rovina / RoofPlane elevations for automatic purlins.
/// Derived from physical timber + rafter geometry — never from schematic SVG/affine edges.
/// </summary>
public static class RoofAutomaticPurlinRoofPlaneRules
{
    /// <summary>
    /// WallPlate / Intermediate: upper rafter face at the member vertical axis.
    /// From the horizontal timber top (outer seating reference), add:
    /// - slope rise from outer face to axis: (width / 2) · tan(pitch)
    /// - remaining perpendicular cover as vertical: (H − D) / cos(pitch)
    /// where D = rafterHeight · percent / 100 (perpendicular from lower face).
    /// </summary>
    public static bool TryResolveMemberRoofPlaneRelativeMm(
        double purlinTopRelativeElevationMm,
        double purlinWidthMm,
        double rafterHeightMm,
        double seatingDepthMm,
        double pitchDegrees,
        out double roofPlaneRelativeMm)
    {
        roofPlaneRelativeMm = 0d;
        if (!IsFinite(purlinTopRelativeElevationMm) ||
            !IsFinite(purlinWidthMm) ||
            purlinWidthMm <= 0d ||
            !IsFinite(rafterHeightMm) ||
            rafterHeightMm <= 0d ||
            !IsFinite(seatingDepthMm) ||
            seatingDepthMm < 0d ||
            seatingDepthMm > rafterHeightMm + 1e-9d ||
            !TryPitchCosSin(pitchDegrees, out var cosPitch, out var sinPitch))
        {
            return false;
        }

        var remainingPerpendicularMm = rafterHeightMm - seatingDepthMm;
        var verticalCoverMm = remainingPerpendicularMm / cosPitch;
        var axisRiseMm = (purlinWidthMm / 2d) * (sinPitch / cosPitch);
        roofPlaneRelativeMm =
            purlinTopRelativeElevationMm + axisRiseMm + verticalCoverMm;
        return IsFinite(roofPlaneRelativeMm);
    }

    /// <summary>
    /// Same member formula expressed in local Z (pass timber top local Z).
    /// </summary>
    public static bool TryResolveMemberRoofPlaneLocalZMm(
        double purlinTopLocalZMm,
        double purlinWidthMm,
        double rafterHeightMm,
        double seatingDepthMm,
        double pitchDegrees,
        out double roofPlaneLocalZMm) =>
        TryResolveMemberRoofPlaneRelativeMm(
            purlinTopLocalZMm,
            purlinWidthMm,
            rafterHeightMm,
            seatingDepthMm,
            pitchDegrees,
            out roofPlaneLocalZMm);

    /// <summary>
    /// Ridge: upper-face apex at the ridge timber vertical axis — same outer-seating
    /// contract as WallPlate/Intermediate:
    /// Top + (width / 2) · tan(pitch) + (H − D) / cos(pitch).
    /// This equals the intersection of the left/right upper rafter faces at the axis
    /// when Top is the outer-corner seating elevation.
    /// </summary>
    public static bool TryResolveRidgeRoofPlaneRelativeMm(
        double purlinTopRelativeElevationMm,
        double purlinWidthMm,
        double rafterHeightMm,
        double seatingDepthMm,
        double pitchDegrees,
        out double roofPlaneRelativeMm) =>
        TryResolveMemberRoofPlaneRelativeMm(
            purlinTopRelativeElevationMm,
            purlinWidthMm,
            rafterHeightMm,
            seatingDepthMm,
            pitchDegrees,
            out roofPlaneRelativeMm);

    public static bool TryResolveRidgeRoofPlaneLocalZMm(
        double purlinTopLocalZMm,
        double purlinWidthMm,
        double rafterHeightMm,
        double seatingDepthMm,
        double pitchDegrees,
        out double roofPlaneLocalZMm) =>
        TryResolveMemberRoofPlaneLocalZMm(
            purlinTopLocalZMm,
            purlinWidthMm,
            rafterHeightMm,
            seatingDepthMm,
            pitchDegrees,
            out roofPlaneLocalZMm);

    public static bool TryResolveFromPlanItem(
        RoofAutomaticPurlinPlanItem? item,
        double pitchDegrees,
        double rafterHeightMm,
        out double roofPlaneRelativeMm)
    {
        roofPlaneRelativeMm = 0d;
        if (item?.ElevationProfile is null || item.PhysicalPlacement is null)
        {
            return false;
        }

        return TryResolveMemberRoofPlaneRelativeMm(
            item.ElevationProfile.TopRelativeElevationMm,
            item.WidthMm,
            rafterHeightMm,
            item.PhysicalPlacement.SeatingDepthMm,
            pitchDegrees,
            out roofPlaneRelativeMm);
    }

    /// <summary>
    /// HOST regression fixture (WallPlateBottom datum, Top_rel = wallPlateHeight):
    /// 125 mm rafter · 45° · 140×140 · 25% → 342.5825… mm → display +0.343 m.
    /// </summary>
    public static double HostWallPlateRoofPlaneRelativeMm(
        double rafterHeightMm,
        double wallPlateHeightMm,
        double wallPlateWidthMm,
        double seatingPercent,
        double pitchDegrees)
    {
        var seatingDepthMm = rafterHeightMm * seatingPercent / 100d;
        if (!TryResolveMemberRoofPlaneRelativeMm(
                wallPlateHeightMm,
                wallPlateWidthMm,
                rafterHeightMm,
                seatingDepthMm,
                pitchDegrees,
                out var relativeMm))
        {
            throw new InvalidOperationException("HOST roof-plane fixture is invalid.");
        }

        return relativeMm;
    }

    private static bool TryPitchCosSin(
        double pitchDegrees,
        out double cosPitch,
        out double sinPitch)
    {
        cosPitch = 0d;
        sinPitch = 0d;
        if (!IsFinite(pitchDegrees) || pitchDegrees < 0d || pitchDegrees >= 90d)
        {
            return false;
        }

        var pitchRad = pitchDegrees * Math.PI / 180d;
        cosPitch = Math.Cos(pitchRad);
        sinPitch = Math.Sin(pitchRad);
        return cosPitch > 1e-12d;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}

