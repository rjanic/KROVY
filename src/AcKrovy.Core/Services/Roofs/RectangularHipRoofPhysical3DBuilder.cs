using System.Globalization;
using System.Text;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public enum RectangularHipRoofPhysical3DBuildError
{
    None = 0,
    Ineligible,
    InvalidElevation,
    NonFiniteGeometry,
}

public sealed record RectangularHipRoofPhysical3DBuildResult(
    bool IsValid,
    RoofPhysical3DModel? Model,
    RectangularHipRoofPhysical3DBuildError Error,
    RectangularSymmetricHipEligibilityError EligibilityError);

/// <summary>
/// Builds the authoritative physical 3D hip model from shared topology + absolute elevation.
/// WCS Z = ResolvedEaveRelativeMm + topologyLocalZ (never polyline elevation).
/// </summary>
public static class RectangularHipRoofPhysical3DBuilder
{
    public static RectangularHipRoofPhysical3DBuildResult TryBuild(
        string ownerReference,
        RoofFootprint footprint,
        HipRoofGeometry geometry,
        RoofAbsoluteElevationState elevation)
    {
        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            throw new ArgumentException("Owner reference is required.", nameof(ownerReference));
        }

        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (elevation is null)
        {
            throw new ArgumentNullException(nameof(elevation));
        }

        if (!IsFinite(elevation.ResolvedEaveRelativeElevationMm) ||
            !IsFinite(elevation.ResolvedRidgeRelativeElevationMm) ||
            !IsFinite(elevation.RiseMm))
        {
            return Fail(RectangularHipRoofPhysical3DBuildError.InvalidElevation);
        }

        var eligibility = RectangularSymmetricHipEligibility.Evaluate(footprint, geometry);
        if (!eligibility.IsEligible)
        {
            return new RectangularHipRoofPhysical3DBuildResult(
                false,
                null,
                RectangularHipRoofPhysical3DBuildError.Ineligible,
                eligibility.Error);
        }

        var eaveOffset = elevation.ResolvedEaveRelativeElevationMm;
        var vertices = geometry.Topology.Nodes
            .Select(point => Offset(point, eaveOffset))
            .ToArray();
        if (vertices.Any(point => !IsFinite(point)))
        {
            return Fail(
                RectangularHipRoofPhysical3DBuildError.NonFiniteGeometry,
                eligibility.Error);
        }

        RoofPoint3D MapLocal(RoofPoint3D local) => Offset(local, eaveOffset);

        var eaves = new List<RoofPhysical3DEdge>(4);
        var hips = new List<RoofPhysical3DEdge>(4);
        var ridges = new List<RoofPhysical3DEdge>(1);
        foreach (var edge in geometry.Topology.Edges)
        {
            var segment = new RoofSegment3D(
                MapLocal(geometry.Topology.Nodes[edge.StartNodeIndex]),
                MapLocal(geometry.Topology.Nodes[edge.EndNodeIndex]));
            switch (edge.Kind)
            {
                case RoofTopologyEdgeKind.Eave:
                    eaves.Add(new RoofPhysical3DEdge(
                        RoofPhysical3DEdgeKind.Eave,
                        BuildEdgeId(ownerReference, "Eave", edge.StartNodeIndex, edge.EndNodeIndex),
                        segment));
                    break;
                case RoofTopologyEdgeKind.Hip:
                    hips.Add(new RoofPhysical3DEdge(
                        RoofPhysical3DEdgeKind.Hip,
                        BuildEdgeId(ownerReference, "Hip", edge.StartNodeIndex, edge.EndNodeIndex),
                        segment));
                    break;
                case RoofTopologyEdgeKind.Ridge:
                    if (segment.LengthMm >
                        SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
                    {
                        ridges.Add(new RoofPhysical3DEdge(
                            RoofPhysical3DEdgeKind.Ridge,
                            BuildEdgeId(ownerReference, "Ridge", edge.StartNodeIndex, edge.EndNodeIndex),
                            segment));
                    }

                    break;
            }
        }

        var faces = new List<RoofPhysical3DFace>(4);
        foreach (var face in geometry.Faces)
        {
            var polygon = face.BoundaryPoints.Select(MapLocal).ToArray();
            var planeId = new RoofPhysical3DPlaneId(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|Plane|S{1}",
                    ownerReference,
                    face.SourceEdgeIndex));
            faces.Add(new RoofPhysical3DFace(
                planeId,
                face.SourceEdgeIndex,
                face.SlopeDegrees,
                Array.AsReadOnly(polygon),
                new RoofSegment3D(MapLocal(face.Eave.Start), MapLocal(face.Eave.End))));
        }

        var model = new RoofPhysical3DModel(
            ownerReference,
            geometry.PrimarySlopeDegrees,
            elevation,
            IsPyramidal: eligibility.Kind ==
                RectangularSymmetricHipEligibilityKind.SquareOrCollapsedNearSquare,
            Array.AsReadOnly(vertices),
            Array.AsReadOnly(eaves.ToArray()),
            Array.AsReadOnly(hips.ToArray()),
            Array.AsReadOnly(ridges.ToArray()),
            Array.AsReadOnly(faces.ToArray()),
            BuildGenerationSignature(
                ownerReference,
                geometry.PrimarySlopeDegrees,
                elevation,
                vertices,
                faces));
        return new RectangularHipRoofPhysical3DBuildResult(
            true,
            model,
            RectangularHipRoofPhysical3DBuildError.None,
            RectangularSymmetricHipEligibilityError.None);
    }

    private static RectangularHipRoofPhysical3DBuildResult Fail(
        RectangularHipRoofPhysical3DBuildError error,
        RectangularSymmetricHipEligibilityError eligibilityError =
            RectangularSymmetricHipEligibilityError.None) =>
        new(false, null, error, eligibilityError);

    private static RoofPoint3D Offset(RoofPoint3D local, double eaveRelativeMm) =>
        new(local.X, local.Y, local.Z + eaveRelativeMm);

    private static string BuildEdgeId(
        string ownerReference,
        string kind,
        int start,
        int end)
    {
        var a = Math.Min(start, end);
        var b = Math.Max(start, end);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}",
            ownerReference,
            kind,
            a,
            b);
    }

    private static string BuildGenerationSignature(
        string ownerReference,
        double pitchDegrees,
        RoofAbsoluteElevationState elevation,
        IReadOnlyList<RoofPoint3D> vertices,
        IReadOnlyList<RoofPhysical3DFace> faces)
    {
        var builder = new StringBuilder();
        builder.Append("Physical3D;");
        builder.Append(ownerReference);
        builder.Append(';');
        builder.Append(Format(pitchDegrees, 10));
        builder.Append(';');
        builder.Append(elevation.InputMode);
        builder.Append(';');
        builder.Append(Format(elevation.ResolvedEaveRelativeElevationMm, 6));
        builder.Append(';');
        builder.Append(elevation.Physical3DEnabled ? "1" : "0");
        builder.Append(";V");
        foreach (var point in vertices)
        {
            builder.Append(';');
            builder.Append(Format(point.X, 6));
            builder.Append(',');
            builder.Append(Format(point.Y, 6));
            builder.Append(',');
            builder.Append(Format(point.Z, 6));
        }

        builder.Append(";F");
        foreach (var face in faces.OrderBy(item => item.SourceEdgeIndex))
        {
            builder.Append(';');
            builder.Append(face.PlaneId.Value);
            builder.Append(':');
            builder.Append(string.Join(
                "-",
                face.Polygon.Select(point =>
                    Format(point.X, 6) + "," + Format(point.Y, 6) + "," + Format(point.Z, 6))));
        }

        return builder.ToString();
    }

    private static string Format(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return (rounded == 0d ? 0d : rounded).ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
