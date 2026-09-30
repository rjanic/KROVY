using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Partitions a shared upper node of exactly two structural timbers with one
/// vertical, plan-derived plane. The axes and their canonical keys stay intact.
/// </summary>
public static class RoofStructuralUpperNodeMiterResolver
{
    private const double Tolerance = 1e-8;

    /// <summary>
    /// A miter reaches the far side of the timber beyond its centerline node.
    /// One millimetre of construction clearance makes the slice non-tangent.
    /// </summary>
    public static bool TryRequiredOverrunMm(
        RoofSegment3D upperAxis, double widthMm,
        RoofStructuralRafterClipPlane plane, out double overrunMm)
    {
        overrunMm = 0d;
        var dx = upperAxis.End.X - upperAxis.Start.X;
        var dy = upperAxis.End.Y - upperAxis.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length <= Tolerance ||
            !Finite(widthMm) || widthMm <= Tolerance ||
            Math.Abs(plane.RetainedNormal.Z) > Tolerance ||
            Math.Abs(plane.Point.X - upperAxis.End.X) > Tolerance ||
            Math.Abs(plane.Point.Y - upperAxis.End.Y) > Tolerance)
            return false;
        var alongX = dx / length;
        var alongY = dy / length;
        var acrossX = -alongY;
        var acrossY = alongX;
        var alongDot = Math.Abs(plane.RetainedNormal.X * alongX +
            plane.RetainedNormal.Y * alongY);
        var acrossDot = Math.Abs(plane.RetainedNormal.X * acrossX +
            plane.RetainedNormal.Y * acrossY);
        if (alongDot <= Tolerance) return false;
        overrunMm = widthMm / 2d * acrossDot / alongDot + 1d;
        return Finite(overrunMm) && overrunMm > 0d;
    }

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    public static bool TryResolve(
        RoofTopology topology,
        IReadOnlyList<ResolvedRoofStructuralEdge> structuralEdges,
        out IReadOnlyDictionary<int, RoofStructuralRafterClipPlane> planes,
        out string failureReason)
    {
        var result = new Dictionary<int, RoofStructuralRafterClipPlane>();
        planes = result;
        failureReason = string.Empty;
        var eligible = structuralEdges.Where(edge => edge.IsAutomaticStructuralTimberEligible &&
            edge.StructuralRole is RoofStructuralRole.Hip or RoofStructuralRole.Valley).ToArray();
        foreach (var group in eligible.GroupBy(edge => UpperNode(topology, edge)))
        {
            if (group.Key < 0 || group.Count() != 2) continue;
            var pair = group.ToArray();
            var node = topology.Nodes[group.Key];
            var first = Away(topology, pair[0], node);
            var second = Away(topology, pair[1], node);
            var firstLength = Math.Sqrt(first.X * first.X + first.Y * first.Y);
            var secondLength = Math.Sqrt(second.X * second.X + second.Y * second.Y);
            if (firstLength <= Tolerance || secondLength <= Tolerance)
            {
                failureReason = "UpperNodeMiterDegenerateAxis";
                return false;
            }
            var ax = first.X / firstLength;
            var ay = first.Y / firstLength;
            var bx = second.X / secondLength;
            var by = second.Y / secondLength;
            var bisectorX = ax + bx;
            var bisectorY = ay + by;
            var bisectorLength = Math.Sqrt(bisectorX * bisectorX + bisectorY * bisectorY);
            if (bisectorLength <= Tolerance)
            {
                failureReason = "UpperNodeMiterOpposedAxes";
                return false;
            }
            var nx = -bisectorY / bisectorLength;
            var ny = bisectorX / bisectorLength;
            var side = nx * ax + ny * ay;
            if (Math.Abs(side) <= Tolerance)
            {
                failureReason = "UpperNodeMiterCoincidentAxes";
                return false;
            }
            if (side < 0d) { nx = -nx; ny = -ny; }
            var point = new RoofPoint3D(node.X, node.Y, node.Z);
            result.Add(pair[0].TopologyEdgeIndex,
                new RoofStructuralRafterClipPlane(point, new RoofPoint3D(nx, ny, 0d)));
            result.Add(pair[1].TopologyEdgeIndex,
                new RoofStructuralRafterClipPlane(point, new RoofPoint3D(-nx, -ny, 0d)));
        }
        return true;
    }

    private static int UpperNode(RoofTopology topology, ResolvedRoofStructuralEdge edge)
    {
        var source = topology.Edges[edge.TopologyEdgeIndex];
        var startIsBoundary = source.StartNodeIndex < topology.BoundaryVertexCount;
        var endIsBoundary = source.EndNodeIndex < topology.BoundaryVertexCount;
        return startIsBoundary == endIsBoundary ? -1 :
            startIsBoundary ? source.EndNodeIndex : source.StartNodeIndex;
    }

    private static RoofPoint3D Away(RoofTopology topology,
        ResolvedRoofStructuralEdge edge, RoofPoint3D node)
    {
        var source = topology.Edges[edge.TopologyEdgeIndex];
        var eaveIndex = source.StartNodeIndex < topology.BoundaryVertexCount
            ? source.StartNodeIndex : source.EndNodeIndex;
        var eave = topology.Nodes[eaveIndex];
        return new RoofPoint3D(eave.X - node.X, eave.Y - node.Y, 0d);
    }
}
