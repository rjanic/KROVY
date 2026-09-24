using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral physical-section geometry for purlin seating under a rafter.
/// The persisted plan-view rafter Line is the XY representation of the mathematical
/// roof face. For automatic-purlin seating that face is the physical UPPER rafter
/// face (SourceEave plane), not the timber centroid. Section thickness is measured
/// perpendicular to the face; at a fixed horizontal station the vertical span of
/// that perpendicular thickness is height / cos(pitch) = height / n_z.
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

    /// <summary>
    /// Builds a fixed-horizontal-station rafter section whose
    /// <paramref name="upperFacePoint"/> lies on the mathematical UPPER face.
    /// Lower / center elevations use vertical span = height / n_z.
    /// </summary>
    public static bool TryCreateRafterSection(
        RoofPoint3D upperFacePoint,
        RoofFaceUnitNormal faceNormal,
        double rafterHeightMm,
        out RoofRafterPhysicalSection? section)
    {
        section = null;
        if (!IsFinite(upperFacePoint) ||
            !IsValidUnitNormal(faceNormal) ||
            !IsFinite(rafterHeightMm) ||
            rafterHeightMm <= 0d)
        {
            return false;
        }

        var verticalHalfSpanMm = rafterHeightMm / (2d * faceNormal.Z);
        var centerPoint = new RoofPoint3D(
            upperFacePoint.X,
            upperFacePoint.Y,
            upperFacePoint.Z - verticalHalfSpanMm);
        var lowerPoint = new RoofPoint3D(
            upperFacePoint.X,
            upperFacePoint.Y,
            upperFacePoint.Z - 2d * verticalHalfSpanMm);
        section = new RoofRafterPhysicalSection(
            centerPoint,
            faceNormal,
            rafterHeightMm,
            lowerPoint,
            upperFacePoint);
        return true;
    }

    /// <summary>
    /// Inverse of <see cref="CreatePurlinPlacement"/> seating: given the desired timber
    /// bottom local Z, recover the mathematical UPPER rafter-face elevation at the
    /// member vertical axis so that seating depth D places Top = Bottom + height.
    /// axisUpper = Top + (H − D) / n_z + (width / 2) · tan(pitch).
    /// </summary>
    public static bool TryResolveUpperFaceLocalZFromSeatedBottom(
        double bottomLocalZMm,
        double purlinHeightMm,
        double purlinWidthMm,
        double rafterHeightMm,
        double seatingDepthMm,
        double pitchDegrees,
        out double upperFaceLocalZMm)
    {
        upperFaceLocalZMm = 0d;
        if (!IsFinite(bottomLocalZMm) ||
            !IsFinite(purlinHeightMm) || purlinHeightMm <= 0d ||
            !IsFinite(purlinWidthMm) || purlinWidthMm < 0d ||
            !IsFinite(rafterHeightMm) || rafterHeightMm <= 0d ||
            !IsFinite(seatingDepthMm) ||
            seatingDepthMm < 0d ||
            seatingDepthMm > rafterHeightMm ||
            !IsFinite(pitchDegrees) ||
            pitchDegrees < 0d ||
            pitchDegrees >= 90d)
        {
            return false;
        }

        var pitchRad = pitchDegrees * Math.PI / 180d;
        var cosPitch = Math.Cos(pitchRad);
        var tanPitch = Math.Tan(pitchRad);
        if (!IsFinite(cosPitch) || cosPitch <= UnitTolerance || !IsFinite(tanPitch))
        {
            return false;
        }

        var topLocalZMm = bottomLocalZMm + purlinHeightMm;
        upperFaceLocalZMm =
            topLocalZMm +
            (rafterHeightMm - seatingDepthMm) / cosPitch +
            (purlinWidthMm / 2d) * tanPitch;
        return IsFinite(upperFaceLocalZMm);
    }

    public static RoofPurlinPhysicalPlacementResult CreatePurlinPlacement(
        RoofPoint3D upperFacePoint,
        RoofFaceUnitNormal faceNormal,
        double rafterHeightMm,
        double purlinHeightMm,
        double seatingDepthMm,
        RoofRelativeElevationDatum? datum,
        double purlinWidthMm = 0d)
    {
        if (!IsFinite(upperFacePoint))
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
            seatingDepthMm < 0d ||
            seatingDepthMm > rafterHeightMm)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidSeatingDepth);
        }
        if (!IsFinite(purlinWidthMm) || purlinWidthMm < 0d)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }
        if (datum is null || !RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                datum.ReferenceKind,
                datum.ReferenceRelativeElevationMm,
                datum.ReferenceLocalZMm).IsValid)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.InvalidRelativeElevationDatum);
        }

        // Outer top corner (eave-side / downslope): evaluate the upper-face Z at the
        // outer plan station (axis Z − (width/2)·tan(pitch)). Width 0 keeps axis station.
        var outerUpperFacePoint = upperFacePoint;
        if (purlinWidthMm > UnitTolerance)
        {
            var horizontal = Math.Sqrt(Math.Max(0d, 1d - faceNormal.Z * faceNormal.Z));
            var tanPitch = horizontal / faceNormal.Z;
            outerUpperFacePoint = new RoofPoint3D(
                upperFacePoint.X,
                upperFacePoint.Y,
                upperFacePoint.Z - (purlinWidthMm / 2d) * tanPitch);
            if (!IsFinite(outerUpperFacePoint))
            {
                return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
            }
        }

        if (!TryCreateRafterSection(
                outerUpperFacePoint,
                faceNormal,
                rafterHeightMm,
                out var section) ||
            section is null)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }

        // D is perpendicular from the lower face toward the upper face. At a fixed
        // horizontal station the matching vertical rise is D / cos(pitch) = D / n_z.
        // D=0 → top at lower face; D=H → top at upper face.
        double top;
        if (seatingDepthMm <= UnitTolerance)
        {
            top = section.LowerSurfacePoint.Z;
        }
        else if (Math.Abs(seatingDepthMm - rafterHeightMm) <= UnitTolerance)
        {
            top = section.UpperSurfacePoint.Z;
        }
        else
        {
            top = section.UpperSurfacePoint.Z -
                  (rafterHeightMm - seatingDepthMm) / faceNormal.Z;
        }

        var center = top - purlinHeightMm / 2d;
        var bottom = center - purlinHeightMm / 2d;
        if (!IsFinite(top) || !IsFinite(center) || !IsFinite(bottom) ||
            !(section.LowerSurfacePoint.Z - UnitTolerance <= top &&
              top <= section.UpperSurfacePoint.Z + UnitTolerance))
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }

        double Relative(double localZ) =>
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, localZ);

        // Axis-station rafter elevations remain reported at the member vertical axis.
        if (!TryCreateRafterSection(
                upperFacePoint,
                faceNormal,
                rafterHeightMm,
                out var axisSection) ||
            axisSection is null)
        {
            return Invalid(RoofPurlinPhysicalPlacementError.ImpossiblePlacement);
        }

        return new RoofPurlinPhysicalPlacementResult(
            true,
            new RoofPurlinPhysicalPlacement(
                axisSection,
                axisSection.CenterlinePoint.Z,
                axisSection.LowerSurfacePoint.Z,
                axisSection.UpperSurfacePoint.Z,
                seatingDepthMm,
                bottom,
                center,
                top,
                Relative(axisSection.CenterlinePoint.Z),
                Relative(axisSection.LowerSurfacePoint.Z),
                Relative(axisSection.UpperSurfacePoint.Z),
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

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofPurlinPhysicalPlacementResult Invalid(
        RoofPurlinPhysicalPlacementError error) => new(false, null, error);
}
