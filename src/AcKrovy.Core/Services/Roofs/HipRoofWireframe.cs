using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Maps generic Hip <see cref="RoofTopology"/> skeleton edges onto the shared
/// permanent-display role model. Eaves and coplanar seams are omitted because the
/// source footprint already shows the boundary.
/// </summary>
public static class HipRoofWireframe
{
    public const int MaxRidgeRoles = 16;
    public const int MaxHipRoles = 48;
    public const int MaxValleyRoles = 32;

    public static IReadOnlyList<RoofDisplayEdge> Create(
        HipRoofGeometry geometry,
        double sourceElevation)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (!IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }

        var topology = geometry.Topology;
        var ridges = Collect(topology, RoofTopologyEdgeKind.Ridge);
        var hips = Collect(topology, RoofTopologyEdgeKind.Hip);
        var valleys = Collect(topology, RoofTopologyEdgeKind.Valley);
        if (ridges.Count > MaxRidgeRoles ||
            hips.Count > MaxHipRoles ||
            valleys.Count > MaxValleyRoles)
        {
            throw new ArgumentException(
                "Hip topology exceeds the permanent-display role capacity.",
                nameof(geometry));
        }

        var edges = new List<RoofDisplayEdge>(ridges.Count + hips.Count + valleys.Count);
        for (var i = 0; i < ridges.Count; i++)
        {
            edges.Add(new RoofDisplayEdge(
                (RoofDisplayEdgeRole)((int)RoofDisplayEdgeRole.HipRidge00 + i),
                Segment(topology, ridges[i], sourceElevation)));
        }

        for (var i = 0; i < hips.Count; i++)
        {
            edges.Add(new RoofDisplayEdge(
                (RoofDisplayEdgeRole)((int)RoofDisplayEdgeRole.Hip00 + i),
                Segment(topology, hips[i], sourceElevation)));
        }

        for (var i = 0; i < valleys.Count; i++)
        {
            edges.Add(new RoofDisplayEdge(
                (RoofDisplayEdgeRole)((int)RoofDisplayEdgeRole.HipValley00 + i),
                Segment(topology, valleys[i], sourceElevation)));
        }

        if (edges.Count == 0)
        {
            throw new ArgumentException("Hip topology produced no displayable edges.", nameof(geometry));
        }

        return edges;
    }

    public static bool IsRidgeRole(RoofDisplayEdgeRole role) =>
        role == RoofDisplayEdgeRole.Ridge ||
        role is >= RoofDisplayEdgeRole.HipRidge00 and <= RoofDisplayEdgeRole.HipRidge15;

    public static bool IsHipTopologyRole(RoofDisplayEdgeRole role) =>
        role is >= RoofDisplayEdgeRole.HipRidge00 and <= RoofDisplayEdgeRole.HipValley31;

    private static IReadOnlyList<RoofTopologyEdge> Collect(
        RoofTopology topology,
        RoofTopologyEdgeKind kind) =>
        topology.Edges
            .Where(edge => edge.Kind == kind)
            .OrderBy(edge => Math.Min(edge.StartNodeIndex, edge.EndNodeIndex))
            .ThenBy(edge => Math.Max(edge.StartNodeIndex, edge.EndNodeIndex))
            .ToArray();

    private static RoofSegment3D Segment(
        RoofTopology topology,
        RoofTopologyEdge edge,
        double sourceElevation)
    {
        var start = topology.Nodes[edge.StartNodeIndex];
        var end = topology.Nodes[edge.EndNodeIndex];
        return new RoofSegment3D(
            new RoofPoint3D(start.X, start.Y, start.Z + sourceElevation),
            new RoofPoint3D(end.X, end.Y, end.Z + sourceElevation));
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
