using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Builds a minimal ordinary-rafter <see cref="RoofTopology"/> from SimpleGable geometry.
/// Two faces, two eaves, one shared ridge; zero Hip/Valley edges. Intended for ordinary
/// Physical3D only — not Structural Hip/Valley timber.
/// </summary>
public static class SimpleGableRoofTopologyAdapter
{
    public static bool TryCreate(
        SimpleGableRoofGeometry geometry,
        out RoofTopology topology,
        out string failureReason)
    {
        topology = null!;
        failureReason = "InvalidSimpleGableTopology";
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        // Milestone: SimpleGable only. Asymmetric shares the type but is out of scope.
        if (geometry.Kind != RoofKind.SimpleGable ||
            geometry.Faces.Count != 2 ||
            geometry.Faces[0].Index != 0 ||
            geometry.Faces[1].Index != 1)
        {
            failureReason = "UnsupportedGableKindOrFaces";
            return false;
        }

        var face0 = geometry.Faces[0];
        var face1 = geometry.Faces[1];
        var eave0 = face0.Eave;
        var eave1 = face1.Eave;
        var ridge = geometry.Ridge;
        if (!Finite(eave0) || !Finite(eave1) || !Finite(ridge) ||
            eave0.LengthMm <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm ||
            eave1.LengthMm <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm ||
            ridge.LengthMm <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
        {
            failureReason = "DegenerateGableSegments";
            return false;
        }

        // Boundary nodes 0..3 at eaves; internal ridge nodes 4..5.
        // Keep each eave oriented with the ridge direction so FaceIndices stay stable.
        var eave0Canon = CanonicalAlong(eave0, geometry.RidgeDirection);
        var eave1Canon = CanonicalAlong(eave1, geometry.RidgeDirection);
        var ridgeCanon = CanonicalAlong(ridge, geometry.RidgeDirection);

        var nodes = new[]
        {
            eave0Canon.Start,
            eave0Canon.End,
            eave1Canon.Start,
            eave1Canon.End,
            ridgeCanon.Start,
            ridgeCanon.End,
        };

        // Face0 cycle: ridgeStart → eave0Start → eave0End → ridgeEnd (upward Z).
        // Face1 cycle: ridgeStart → ridgeEnd → eave1End → eave1Start (upward Z).
        var face0Nodes = new[] { 4, 0, 1, 5 };
        var face1Nodes = new[] { 4, 5, 3, 2 };
        if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                nodes[face0Nodes[0]], nodes[face0Nodes[1]], nodes[face0Nodes[2]], out var n0) ||
            n0.Z <= 0d ||
            !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                nodes[face1Nodes[0]], nodes[face1Nodes[1]], nodes[face1Nodes[2]], out var n1) ||
            n1.Z <= 0d)
        {
            failureReason = "NonUpwardGableFaceNormal";
            return false;
        }

        var edges = new[]
        {
            new RoofTopologyEdge(0, 1, RoofTopologyEdgeKind.Eave, [0]),
            new RoofTopologyEdge(2, 3, RoofTopologyEdgeKind.Eave, [1]),
            new RoofTopologyEdge(4, 5, RoofTopologyEdgeKind.Ridge, [0, 1]),
            // Gable rakes are face-boundary only — not Hip/Valley timber folds.
            new RoofTopologyEdge(0, 4, RoofTopologyEdgeKind.CoplanarSeam, [0]),
            new RoofTopologyEdge(1, 5, RoofTopologyEdgeKind.CoplanarSeam, [0]),
            new RoofTopologyEdge(2, 4, RoofTopologyEdgeKind.CoplanarSeam, [1]),
            new RoofTopologyEdge(3, 5, RoofTopologyEdgeKind.CoplanarSeam, [1]),
        };

        var faces = new[]
        {
            new RoofTopologyFace(0, face0Nodes),
            new RoofTopologyFace(1, face1Nodes),
        };

        topology = new RoofTopology(
            nodes,
            edges,
            faces,
            boundaryVertexCount: 4,
            pitchDegrees: geometry.PrimarySlopeDegrees);

        if (topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip) != 0 ||
            topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley) != 0)
        {
            failureReason = "UnexpectedHipOrValleyEdge";
            topology = null!;
            return false;
        }

        var ridgeEdges = topology.Edges.Where(edge => edge.Kind == RoofTopologyEdgeKind.Ridge).ToArray();
        if (ridgeEdges.Length != 1 || ridgeEdges[0].FaceIndices.Count != 2)
        {
            failureReason = "InvalidSharedRidge";
            topology = null!;
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static RoofSegment3D CanonicalAlong(RoofSegment3D segment, RoofDirection2D ridgeDirection)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        if (dx * ridgeDirection.X + dy * ridgeDirection.Y < 0d)
        {
            return new RoofSegment3D(segment.End, segment.Start);
        }

        return segment;
    }

    private static bool Finite(RoofSegment3D segment) =>
        Finite(segment.Start) && Finite(segment.End);

    private static bool Finite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
