using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Roof-plane half-spaces at a convex upper Hip junction.</summary>
internal static class RoofStructuralRafterRoofEnvelope
{
    internal static bool TryResolve(RoofTopology topology, RoofTopologyEdge edge,
        double physicalEaveElevationMm,
        out IReadOnlyList<RoofStructuralRafterClipPlane> planes)
    {
        planes = Array.Empty<RoofStructuralRafterClipPlane>();
        if (edge.Kind != RoofTopologyEdgeKind.Hip) return true;
        var startIsEave = edge.StartNodeIndex < topology.BoundaryVertexCount;
        var endIsEave = edge.EndNodeIndex < topology.BoundaryVertexCount;
        if (startIsEave == endIsEave) return false;
        var upperNode = startIsEave ? edge.EndNodeIndex : edge.StartNodeIndex;
        var incident = topology.Faces.Where(face =>
            face.BoundaryNodeIndices.Contains(upperNode)).ToArray();
        if (incident.Length < 2) return false;
        var result = new List<RoofStructuralRafterClipPlane>(incident.Length);
        foreach (var face in incident)
        {
            if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology, face, out var normal)) return false;
            var origin = topology.Nodes[face.BoundaryNodeIndices[0]];
            result.Add(new RoofStructuralRafterClipPlane(
                new RoofPoint3D(origin.X, origin.Y,
                    origin.Z + physicalEaveElevationMm),
                new RoofPoint3D(-normal.X, -normal.Y, -normal.Z)));
        }
        planes = result.AsReadOnly();
        return true;
    }
}
