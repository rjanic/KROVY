using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral physical-section geometry. The persisted plan-view rafter Line is the
/// XY representation of a centroidal longitudinal axis reconstructed on the
/// mathematical roof face; this helper never changes the Line's existing WCS Z.
/// Section height follows the upward face normal. Rafter width is tangential to the
/// roof face and perpendicular to the longitudinal axis; it does not affect this
/// height-only seating calculation.
/// </summary>
public static class RoofRafterPhysicalGeometry
{
    public const double UnitTolerance = 1e-9d;

    public static bool TryCreateUpwardUnitNormal(
        RoofTopology? topology,
        RoofTopologyFace? face,
        out RoofFaceUnitNormal normal)
    {
        normal = default;
        if (topology is null || face is null ||
            face.BoundaryNodeIndices.Count < 3 ||
            face.BoundaryNodeIndices.Any(index => index < 0 || index >= topology.Nodes.Count))
        {
            return false;
        }

        var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
        var first = topology.Nodes[face.BoundaryNodeIndices[1]];
        for (var index = 2; index < face.BoundaryNodeIndices.Count; index++)
        {
            var second = topology.Nodes[face.BoundaryNodeIndices[index]];
            if (TryCreateUpwardUnitNormal(origin, first, second, out normal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryCreateUpwardUnitNormal(
        RoofPoint3D first,
        RoofPoint3D second,
        RoofPoint3D third,
        out RoofFaceUnitNormal normal)
    {
        normal = default;
        if (!IsFinite(first) || !IsFinite(second) || !IsFinite(third))
        {
            return false;
        }

        var ux = second.X - first.X;
        var uy = second.Y - first.Y;
        var uz = second.Z - first.Z;
        var vx = third.X - first.X;
        var vy = third.Y - first.Y;
        var vz = third.Z - first.Z;
        var nx = uy * vz - uz * vy;
        var ny = uz * vx - ux * vz;
        var nz = ux * vy - uy * vx;
        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (!IsFinite(length) || length <= UnitTolerance)
        {
            return false;
        }

        if (nz < 0d)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }

        normal = new RoofFaceUnitNormal(nx / length, ny / length, nz / length);
        return IsValidUnitNormal(normal);
    }

    public static bool TryCreateRafterSection(
        RoofPoint3D centerlinePoint,
        RoofFaceUnitNormal faceNormal,
        double rafterHeightMm,
        out RoofRafterPhysicalSection? section)
    {
        section = null;
        if (!IsFinite(centerlinePoint) ||
            !IsValidUnitNormal(faceNormal) ||
            !IsFinite(rafterHeightMm) ||
            rafterHeightMm <= 0d)
        {
            return false;
        }

        var halfHeight = rafterHeightMm / 2d;
        var offset = Scale(faceNormal, halfHeight);
        section = new RoofRafterPhysicalSection(
            centerlinePoint,
            faceNormal,
            rafterHeightMm,
            Subtract(centerlinePoint, offset),
            Add(centerlinePoint, offset));
        return true;
    }

    public static RoofPurlinPhysicalPlacementResult CreatePurlinPlacement(
        RoofPoint3D rafterCenterlinePoint,
        RoofFaceUnitNormal faceNormal,
        double rafterHeightMm,
        double purlinHeightMm,
        double seatingDepthMm,
        RoofRelativeElevationDatum? datum)
    {
        if (!IsFinite(rafterCenterlinePoint))
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidCenterlinePoint);
        }
        if (!IsValidUnitNormal(faceNormal))
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidFaceNormal);
        }
        if (!IsFinite(rafterHeightMm) || rafterHeightMm <= 0d)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidRafterHeight);
        }
        if (!IsFinite(purlinHeightMm) || purlinHeightMm <= 0d)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidPurlinHeight);
        }
        if (!IsFinite(seatingDepthMm) ||
            seatingDepthMm <= 0d ||
            seatingDepthMm >= rafterHeightMm)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidSeatingDepth);
        }
        if (datum is null || !RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                datum.ReferenceKind,
                datum.ReferenceRelativeElevationMm,
                datum.ReferenceLocalZMm).IsValid)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidRelativeElevationDatum);
        }
        if (!TryCreateRafterSection(
                rafterCenterlinePoint,
                faceNormal,
                rafterHeightMm,
                out var section) ||
            section is null)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }

        // Travel D from the lower surface along the roof normal. The reached point
        // lies on the horizontal purlin top plane, hence only its Z is used below.
        var supportPoint = Add(
            section.LowerSurfacePoint,
            Scale(faceNormal, seatingDepthMm));
        var top = supportPoint.Z;
        var center = top - purlinHeightMm / 2d;
        var bottom = center - purlinHeightMm / 2d;
        if (!IsFinite(top) || !IsFinite(center) || !IsFinite(bottom) ||
            !(section.LowerSurfacePoint.Z < top && top < section.UpperSurfacePoint.Z))
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }

        double Relative(double localZ) =>
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, localZ);

        return new RoofPurlinPhysicalPlacementResult(
            true,
            new RoofPurlinPhysicalPlacement(
                section,
                rafterCenterlinePoint.Z,
                section.LowerSurfacePoint.Z,
                section.UpperSurfacePoint.Z,
                seatingDepthMm,
                bottom,
                center,
                top,
                Relative(rafterCenterlinePoint.Z),
                Relative(section.LowerSurfacePoint.Z),
                Relative(section.UpperSurfacePoint.Z),
                Relative(bottom),
                Relative(center),
                Relative(top)),
            RoofPurlinPhysicalPlacementError.None);
    }

    public static bool IsValidUnitNormal(RoofFaceUnitNormal normal)
    {
        if (!IsFinite(normal.X) || !IsFinite(normal.Y) || !IsFinite(normal.Z) ||
            normal.Z <= UnitTolerance)
        {
            return false;
        }

        return Math.Abs(normal.Length - 1d) <= UnitTolerance;
    }

    private static RoofPoint3D Scale(RoofFaceUnitNormal normal, double value) =>
        new(normal.X * value, normal.Y * value, normal.Z * value);

    private static RoofPoint3D Add(RoofPoint3D point, RoofPoint3D vector) =>
        new(point.X + vector.X, point.Y + vector.Y, point.Z + vector.Z);

    private static RoofPoint3D Subtract(RoofPoint3D point, RoofPoint3D vector) =>
        new(point.X - vector.X, point.Y - vector.Y, point.Z - vector.Z);

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofPurlinPhysicalPlacementResult Invalid(
        RoofPurlinPhysicalPlacementError error) => new(false, null, error);
}
