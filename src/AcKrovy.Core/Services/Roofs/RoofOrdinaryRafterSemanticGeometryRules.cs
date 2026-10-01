using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Resolve final semantic endpoints against topology, independently of station identity.</summary>
public static class RoofOrdinaryRafterSemanticGeometryRules
{
    public static RoofFaceRafterSegment ResolveSegment(RoofTopology topology,
        RoofFaceRafterSegment anchor, RoofSegment3D axis)
    {
        var start = new RoofPoint2D(axis.Start.X, axis.Start.Y);
        var end = new RoofPoint2D(axis.End.X, axis.End.Y);
        // Prefer the exact anchor face when geometry belongs to multiple coplanar/boundary faces.
        // Explicit edits outside the footprint retain the anchor-plane extension, as existing MOVE does.
        var face = topology.Faces.OrderBy(item => item.SourceEdgeIndex == anchor.SourceFaceIndex ? 0 : 1)
            .ThenBy(item => item.SourceEdgeIndex).FirstOrDefault(item =>
                RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
                    start, end,
                    item.BoundaryNodeIndices.Select(index => new RoofPoint2D(
                        topology.Nodes[index].X, topology.Nodes[index].Y)).ToArray()));
        var faceIndex = face?.SourceEdgeIndex ?? anchor.SourceFaceIndex;
        return anchor with
        {
            SourceFaceIndex = faceIndex,
            PlanStart = start,
            PlanEnd = end,
            PlanLengthMm = start.DistanceTo(end),
            StartBoundaryRole = Boundary(topology, faceIndex, start),
            EndBoundaryRole = Boundary(topology, faceIndex, end),
        };
    }

    private static RoofRafterBoundaryRole Boundary(RoofTopology topology, int face, RoofPoint2D point)
    {
        foreach (var edge in topology.Edges.Where(item => item.FaceIndices.Contains(face)))
        {
            var a = topology.Nodes[edge.StartNodeIndex];
            var b = topology.Nodes[edge.EndNodeIndex];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var px = point.X - a.X;
            var py = point.Y - a.Y;
            var tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;
            if (length <= tolerance || Math.Abs(px * dy - py * dx) > tolerance * length ||
                px * dx + py * dy < -tolerance * length ||
                px * dx + py * dy > length * length + tolerance * length) continue;
            if (edge.Kind == RoofTopologyEdgeKind.Eave) return RoofRafterBoundaryRole.Eave;
            if (edge.Kind == RoofTopologyEdgeKind.Ridge) return RoofRafterBoundaryRole.Ridge;
            if (edge.Kind == RoofTopologyEdgeKind.Hip) return RoofRafterBoundaryRole.Hip;
            if (edge.Kind == RoofTopologyEdgeKind.Valley) return RoofRafterBoundaryRole.Valley;
        }
        return RoofRafterBoundaryRole.Free;
    }
}
