using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Physical roof-fold classification for structural timber eligibility.
/// Horizontal Ridge remains topology/reference only; convex folds become HipRafter
/// and concave folds become ValleyRafter when the exposed plane intersection is valid.
/// </summary>
public enum RoofPhysicalStructuralFoldClass
{
    Invalid = 0,
    HorizontalRidge = 1,
    ConvexHip = 2,
    ConcaveValley = 3,
}

/// <summary>
/// Geometry-only fold checks. No absolute-coordinate, footprint-name, winding,
/// or BoundaryEdgeId heuristics.
/// </summary>
public static class RoofPhysicalStructuralFold
{
    public static double CoordinateToleranceMm =>
        SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;

    public static bool IsHorizontalRidge(RoofTopology topology, RoofTopologyEdge edge)
    {
        if (edge.Kind != RoofTopologyEdgeKind.Ridge)
        {
            return false;
        }

        var start = topology.Nodes[edge.StartNodeIndex];
        var end = topology.Nodes[edge.EndNodeIndex];
        return Math.Abs(start.Z - end.Z) <= CoordinateToleranceMm;
    }

    public static bool TryClassify(
        RoofTopology topology,
        RoofTopologyEdge edge,
        out RoofPhysicalStructuralFoldClass foldClass)
    {
        foldClass = RoofPhysicalStructuralFoldClass.Invalid;
        if (!IsGeometricallyValidExposedFold(topology, edge))
        {
            return false;
        }

        if (IsHorizontalRidge(topology, edge))
        {
            foldClass = RoofPhysicalStructuralFoldClass.HorizontalRidge;
            return true;
        }

        foldClass = edge.Kind switch
        {
            RoofTopologyEdgeKind.Valley => RoofPhysicalStructuralFoldClass.ConcaveValley,
            RoofTopologyEdgeKind.Hip => RoofPhysicalStructuralFoldClass.ConvexHip,
            // Inclined topology Ridge is a convex roof-plane fold and may become Hip timber.
            RoofTopologyEdgeKind.Ridge => RoofPhysicalStructuralFoldClass.ConvexHip,
            _ => RoofPhysicalStructuralFoldClass.Invalid,
        };
        return foldClass != RoofPhysicalStructuralFoldClass.Invalid;
    }

    public static bool IsTimberEligibleFold(
        RoofTopology topology,
        RoofTopologyEdge edge,
        RoofStructuralRole role)
    {
        if (role is not (RoofStructuralRole.Hip or RoofStructuralRole.Valley))
        {
            return false;
        }

        if (!TryClassify(topology, edge, out var foldClass))
        {
            return false;
        }

        return role switch
        {
            RoofStructuralRole.Hip => foldClass == RoofPhysicalStructuralFoldClass.ConvexHip,
            RoofStructuralRole.Valley => foldClass == RoofPhysicalStructuralFoldClass.ConcaveValley,
            _ => false,
        };
    }

    /// <summary>
    /// Physical Hip/Valley member inclination above horizontal from the intersection
    /// of the two adjacent roof planes. Does not copy either face pitch.
    /// Magnitude is invariant under direction reversal of the intersection vector.
    /// </summary>
    public static bool TryMemberInclinationDegreesAboveHorizontal(
        RoofTopology topology,
        RoofTopologyEdge edge,
        out double slopeDegrees)
    {
        slopeDegrees = 0d;
        if (edge.FaceIndices.Count != 2)
        {
            return false;
        }

        var faceA = edge.FaceIndices[0];
        var faceB = edge.FaceIndices[1];
        if (faceA == faceB ||
            faceA < 0 || faceB < 0 ||
            faceA >= topology.Faces.Count ||
            faceB >= topology.Faces.Count ||
            !TryFaceNormal(topology, faceA, out var normalA) ||
            !TryFaceNormal(topology, faceB, out var normalB))
        {
            return false;
        }

        return TryInclinationDegreesFromUpwardPlaneNormals(
            normalA.X,
            normalA.Y,
            normalA.Z,
            normalB.X,
            normalB.Y,
            normalB.Z,
            out slopeDegrees);
    }

    /// <summary>
    /// Inclination of the intersection direction of two upward roof-plane normals.
    /// Supports unequal adjacent pitches; no equal-pitch formula is hard-coded.
    /// </summary>
    public static bool TryInclinationDegreesFromUpwardPlaneNormals(
        double normalAX,
        double normalAY,
        double normalAZ,
        double normalBX,
        double normalBY,
        double normalBZ,
        out double slopeDegrees)
    {
        slopeDegrees = 0d;
        var normalA = new Vec3(normalAX, normalAY, normalAZ);
        var normalB = new Vec3(normalBX, normalBY, normalBZ);
        var lengthA = Length(normalA);
        var lengthB = Length(normalB);
        if (!IsFinite(lengthA) ||
            !IsFinite(lengthB) ||
            lengthA <= CoordinateToleranceMm ||
            lengthB <= CoordinateToleranceMm)
        {
            return false;
        }

        normalA = Scale(normalA, 1d / lengthA);
        normalB = Scale(normalB, 1d / lengthB);
        if (normalA.Z < 0d)
        {
            normalA = new Vec3(-normalA.X, -normalA.Y, -normalA.Z);
        }

        if (normalB.Z < 0d)
        {
            normalB = new Vec3(-normalB.X, -normalB.Y, -normalB.Z);
        }

        var intersection = Cross(normalA, normalB);
        var intersectionLen = Length(intersection);
        if (!IsFinite(intersectionLen) || intersectionLen <= CoordinateToleranceMm)
        {
            return false;
        }

        var direction = Scale(intersection, 1d / intersectionLen);
        slopeDegrees = InclinationDegreesAboveHorizontal(direction.X, direction.Y, direction.Z);
        return IsFinite(slopeDegrees) &&
            slopeDegrees >= 0d &&
            slopeDegrees < MaximumMemberSlopeDegreesExclusive;
    }

    /// <summary>
    /// Positive inclination of a 3D direction above horizontal. Endpoint / direction
    /// reversal does not change the magnitude.
    /// </summary>
    public static double InclinationDegreesAboveHorizontal(
        double directionX,
        double directionY,
        double directionZ)
    {
        var horizontal = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        return Math.Atan2(Math.Abs(directionZ), horizontal) * (180d / Math.PI);
    }

    /// <summary>Matches timber slope domain without referencing presentation/length rules.</summary>
    private const double MaximumMemberSlopeDegreesExclusive = 89.9d;

    public static bool IsGeometricallyValidExposedFold(
        RoofTopology topology,
        RoofTopologyEdge edge)
    {
        if (edge.FaceIndices.Count != 2)
        {
            return false;
        }

        var faceA = edge.FaceIndices[0];
        var faceB = edge.FaceIndices[1];
        if (faceA == faceB ||
            faceA < 0 || faceB < 0 ||
            faceA >= topology.Faces.Count ||
            faceB >= topology.Faces.Count)
        {
            return false;
        }

        var start = topology.Nodes[edge.StartNodeIndex];
        var end = topology.Nodes[edge.EndNodeIndex];
        var edgeVec = new Vec3(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
        var edgeLen = Length(edgeVec);
        if (!IsFinite(edgeLen) || edgeLen <= CoordinateToleranceMm)
        {
            return false;
        }

        if (!TryFaceNormal(topology, faceA, out var normalA) ||
            !TryFaceNormal(topology, faceB, out var normalB))
        {
            return false;
        }

        var intersection = Cross(normalA, normalB);
        var intersectionLen = Length(intersection);
        if (!IsFinite(intersectionLen) || intersectionLen <= CoordinateToleranceMm)
        {
            return false;
        }

        var edgeDir = Scale(edgeVec, 1d / edgeLen);
        var intersectionDir = Scale(intersection, 1d / intersectionLen);
        var alignment = Math.Abs(Dot(edgeDir, intersectionDir));
        if (alignment < 1d - 1e-9)
        {
            return false;
        }

        if (!EdgeBelongsToFaceBoundary(topology, faceA, edge) ||
            !EdgeBelongsToFaceBoundary(topology, faceB, edge))
        {
            return false;
        }

        // Full-edge containment: every sample lies on both face boundaries.
        for (var sample = 1; sample <= 3; sample++)
        {
            var t = sample / 4d;
            var point = new RoofPoint3D(
                start.X + edgeVec.X * t,
                start.Y + edgeVec.Y * t,
                start.Z + edgeVec.Z * t);
            if (!PointOnFaceBoundary(topology, faceA, point) ||
                !PointOnFaceBoundary(topology, faceB, point))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EdgeBelongsToFaceBoundary(
        RoofTopology topology,
        int faceIndex,
        RoofTopologyEdge edge)
    {
        var nodes = topology.Faces[faceIndex].BoundaryNodeIndices;
        for (var i = 0; i < nodes.Count; i++)
        {
            var a = nodes[i];
            var b = nodes[(i + 1) % nodes.Count];
            if (a == edge.StartNodeIndex && b == edge.EndNodeIndex ||
                a == edge.EndNodeIndex && b == edge.StartNodeIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointOnFaceBoundary(
        RoofTopology topology,
        int faceIndex,
        RoofPoint3D point)
    {
        var nodes = topology.Faces[faceIndex].BoundaryNodeIndices;
        for (var i = 0; i < nodes.Count; i++)
        {
            var a = topology.Nodes[nodes[i]];
            var b = topology.Nodes[nodes[(i + 1) % nodes.Count]];
            if (PointOnSegment(point, a, b))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointOnSegment(RoofPoint3D point, RoofPoint3D a, RoofPoint3D b)
    {
        var ab = new Vec3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        var ap = new Vec3(point.X - a.X, point.Y - a.Y, point.Z - a.Z);
        var abLen = Length(ab);
        if (abLen <= CoordinateToleranceMm)
        {
            return point.DistanceTo(a) <= CoordinateToleranceMm;
        }

        var abDir = Scale(ab, 1d / abLen);
        var along = Dot(ap, abDir);
        if (along < -CoordinateToleranceMm || along > abLen + CoordinateToleranceMm)
        {
            return false;
        }

        var closest = new RoofPoint3D(
            a.X + abDir.X * along,
            a.Y + abDir.Y * along,
            a.Z + abDir.Z * along);
        return point.DistanceTo(closest) <= CoordinateToleranceMm;
    }

    private static bool TryFaceNormal(
        RoofTopology topology,
        int faceIndex,
        out Vec3 normal)
    {
        normal = default;
        var nodes = topology.Faces[faceIndex].BoundaryNodeIndices;
        if (nodes.Count < 3)
        {
            return false;
        }

        var origin = topology.Nodes[nodes[0]];
        for (var i = 1; i + 1 < nodes.Count; i++)
        {
            var b = topology.Nodes[nodes[i]];
            var c = topology.Nodes[nodes[i + 1]];
            var candidate = Cross(
                new Vec3(b.X - origin.X, b.Y - origin.Y, b.Z - origin.Z),
                new Vec3(c.X - origin.X, c.Y - origin.Y, c.Z - origin.Z));
            var len = Length(candidate);
            if (!IsFinite(len) || len <= CoordinateToleranceMm)
            {
                continue;
            }

            normal = Scale(candidate, 1d / len);
            if (normal.Z < 0d)
            {
                normal = new Vec3(-normal.X, -normal.Y, -normal.Z);
            }

            return true;
        }

        return false;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private readonly record struct Vec3(double X, double Y, double Z);

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double Length(Vec3 value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static Vec3 Scale(Vec3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);
}
