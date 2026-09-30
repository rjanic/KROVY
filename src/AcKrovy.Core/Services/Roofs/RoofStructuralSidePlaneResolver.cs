using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Resolves the facing plumb side of the exact Hip/Valley topology edge.</summary>
public static class RoofStructuralSidePlaneResolver
{
    private const double Tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;

    public static bool TryResolve(
        RoofTopology topology,
        int sourceFaceIndex,
        RoofRafterBoundaryRole role,
        RoofPoint2D canonicalEndpoint,
        RoofPoint2D ordinaryInteriorPoint,
        IReadOnlyList<RoofStructuralRafterTrimSource> sources,
        out RoofStructuralRafterSideCut? cut,
        out string failureReason)
    {
        cut = null;
        failureReason = "StructuralTargetNotFound";
        if (topology is null || sources is null ||
            role is not (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley))
        {
            return false;
        }

        var kind = role == RoofRafterBoundaryRole.Hip
            ? RoofTopologyEdgeKind.Hip : RoofTopologyEdgeKind.Valley;
        var candidates = sources.Where(source =>
            source.Role == role &&
            source.TopologyEdgeIndex >= 0 &&
            source.TopologyEdgeIndex < topology.Edges.Count &&
            topology.Edges[source.TopologyEdgeIndex].Kind == kind &&
            topology.Edges[source.TopologyEdgeIndex].FaceIndices.Contains(sourceFaceIndex) &&
            OnEdge(topology.Segment(topology.Edges[source.TopologyEdgeIndex]),
                canonicalEndpoint)).ToArray();
        if (candidates.Length != 1)
        {
            failureReason = candidates.Length == 0
                ? "StructuralTargetNotFound" : "StructuralTargetAmbiguous";
            return false;
        }

        var target = candidates[0];
        var topologicalAxis = topology.Segment(topology.Edges[target.TopologyEdgeIndex]);
        if (!SamePlanAxis(topologicalAxis, target.Axis))
        {
            failureReason = "StructuralAxisMismatch";
            return false;
        }
        return TryCreatePlane(target, ordinaryInteriorPoint, out cut, out failureReason);
    }

    public static bool TryCreatePlane(
        RoofStructuralRafterTrimSource target,
        RoofPoint2D ordinaryInteriorPoint,
        out RoofStructuralRafterSideCut? cut,
        out string failureReason)
    {
        cut = null;
        failureReason = "StructuralTargetInvalid";
        if (target is null || target.Role is not
            (RoofRafterBoundaryRole.Hip or RoofRafterBoundaryRole.Valley))
        {
            return false;
        }
        if (!Finite(target.WidthMm) || target.WidthMm <= Tolerance)
        {
            failureReason = "StructuralWidthInvalid";
            return false;
        }
        var dx = target.Axis.End.X - target.Axis.Start.X;
        var dy = target.Axis.End.Y - target.Axis.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length <= Tolerance)
        {
            failureReason = "StructuralPlanDirectionDegenerate";
            return false;
        }
        var left = new RoofPoint3D(-dy / length, dx / length, 0d);
        var signedSide = left.X * (ordinaryInteriorPoint.X - target.Axis.Start.X) +
            left.Y * (ordinaryInteriorPoint.Y - target.Axis.Start.Y);
        if (!Finite(signedSide) || Math.Abs(signedSide) <= Tolerance)
        {
            failureReason = "StructuralFacingSideDegenerate";
            return false;
        }
        var sign = Math.Sign(signedSide);
        var normal = new RoofPoint3D(sign * left.X, sign * left.Y, 0d);
        var halfWidth = target.WidthMm / 2d;
        cut = new RoofStructuralRafterSideCut(
            target.TopologyEdgeIndex,
            target.Role,
            target.WidthMm,
            new RoofPoint3D(
                target.Axis.Start.X + normal.X * halfWidth,
                target.Axis.Start.Y + normal.Y * halfWidth,
                target.Axis.Start.Z),
            normal,
            Array.Empty<RoofPoint3D>(),
            Array.Empty<RoofPoint3D>(),
            Array.Empty<RoofPoint3D>());
        failureReason = string.Empty;
        return true;
    }

    private static bool OnEdge(RoofSegment3D edge, RoofPoint2D point)
    {
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var squared = dx * dx + dy * dy;
        if (!Finite(squared) || squared <= Tolerance * Tolerance)
        {
            return false;
        }
        var fraction = ((point.X - edge.Start.X) * dx +
            (point.Y - edge.Start.Y) * dy) / squared;
        var missX = point.X - (edge.Start.X + fraction * dx);
        var missY = point.Y - (edge.Start.Y + fraction * dy);
        return fraction >= -Tolerance && fraction <= 1d + Tolerance &&
            Math.Sqrt(missX * missX + missY * missY) <= Tolerance;
    }

    private static bool SamePlanAxis(RoofSegment3D a, RoofSegment3D b) =>
        (SamePlanPoint(a.Start, b.Start) && SamePlanPoint(a.End, b.End)) ||
        (SamePlanPoint(a.Start, b.End) && SamePlanPoint(a.End, b.Start));

    private static bool SamePlanPoint(RoofPoint3D a, RoofPoint3D b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
