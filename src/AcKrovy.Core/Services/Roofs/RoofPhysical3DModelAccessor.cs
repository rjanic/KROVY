using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Read-only CAD-neutral accessor for the authoritative physical 3D hip roof model.
/// Future Core / Plus / Manufacturing / BIM modules reuse this surface.
/// </summary>
public static class RoofPhysical3DModelAccessor
{
    public static bool TryCreateFromSolvedHip(
        string ownerReference,
        RoofFootprint footprint,
        HipRoofGeometry geometry,
        RoofAbsoluteElevationState elevation,
        out RoofPhysical3DModel? model)
    {
        model = null;
        var result = RectangularHipRoofPhysical3DBuilder.TryBuild(
            ownerReference,
            footprint,
            geometry,
            elevation);
        if (!result.IsValid || result.Model is null)
        {
            return false;
        }

        model = result.Model;
        return true;
    }

    public static bool TryCreateFromSolvedHip(
        string ownerReference,
        RoofFootprint footprint,
        HipRoofGeometry geometry,
        RoofPhysicalElevationData? persistedElevation,
        out RoofPhysical3DModel? model)
    {
        model = null;
        if (!RectangularRoofFootprintRules.TryDescribe(footprint, out var rectangle) ||
            rectangle is null)
        {
            return false;
        }

        var rise = RoofAbsoluteElevationRules.RiseMm(
            rectangle.HalfWidthMm,
            geometry.PrimarySlopeDegrees);
        if (!IsFinite(rise))
        {
            return false;
        }

        var state = persistedElevation is null
            ? RoofPhysicalElevationRules.MissingStoreDefault(rise)
            : RoofPhysicalElevationRules.ToState(persistedElevation, rise);
        if (!state.Physical3DEnabled)
        {
            return false;
        }

        return TryCreateFromSolvedHip(
            ownerReference,
            footprint,
            geometry,
            state,
            out model);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
