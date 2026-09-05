using System.Globalization;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>Geometric edge roles, independent of the user-facing roof preset.</summary>
public enum RoofTopologyEdgeKind
{
    Eave,
    Hip,
    Ridge,
    Valley,
}

/// <summary>One shared edge. Face indices identify its one or two incident faces.</summary>
public sealed class RoofTopologyEdge
{
    internal RoofTopologyEdge(int startNodeIndex, int endNodeIndex,
        RoofTopologyEdgeKind kind, IEnumerable<int> faceIndices)
    {
        StartNodeIndex = startNodeIndex;
        EndNodeIndex = endNodeIndex;
        Kind = kind;
        FaceIndices = Array.AsReadOnly(faceIndices.OrderBy(index => index).ToArray());
    }

    public int StartNodeIndex { get; }
    public int EndNodeIndex { get; }
    public RoofTopologyEdgeKind Kind { get; }
    public IReadOnlyList<int> FaceIndices { get; }
}

/// <summary>A planar face with a connected, upward-facing boundary cycle.</summary>
public sealed class RoofTopologyFace
{
    internal RoofTopologyFace(int sourceEdgeIndex, IEnumerable<int> boundaryNodeIndices)
    {
        SourceEdgeIndex = sourceEdgeIndex;
        BoundaryNodeIndices = Array.AsReadOnly(boundaryNodeIndices.ToArray());
    }

    /// <summary>Canonical footprint edge that generated this face; no WCS direction assumption.</summary>
    public int SourceEdgeIndex { get; }

    /// <summary>Starts with the two source-eave nodes; last node connects back to the first.</summary>
    public IReadOnlyList<int> BoundaryNodeIndices { get; }
}

/// <summary>
/// Immutable indexed roof graph. Nodes and edges are shared between faces, with no
/// fixed face count, ridge count or node degree. Geometry is stored only at nodes.
/// This result contains no termination-edit policy, timber policy or persisted identity.
/// </summary>
public sealed class RoofTopology
{
    internal RoofTopology(IEnumerable<RoofPoint3D> nodes, IEnumerable<RoofTopologyEdge> edges,
        IEnumerable<RoofTopologyFace> faces, int boundaryVertexCount, double pitchDegrees)
    {
        Nodes = Array.AsReadOnly(nodes.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        Faces = Array.AsReadOnly(faces.ToArray());
        BoundaryVertexCount = boundaryVertexCount;
        PitchDegrees = pitchDegrees;
        Signature = BuildSignature();
    }

    /// <summary>Canonical footprint vertices first, then internal nodes in local lexicographic order.</summary>
    public IReadOnlyList<RoofPoint3D> Nodes { get; }

    /// <summary>Number of initial contour nodes; independent of the number of roof faces.</summary>
    public int BoundaryVertexCount { get; }

    /// <summary>Eaves in source order, then skeleton edges ordered by endpoint indices.</summary>
    public IReadOnlyList<RoofTopologyEdge> Edges { get; }

    /// <summary>One face per canonical source edge in the current all-eave construction.</summary>
    public IReadOnlyList<RoofTopologyFace> Faces { get; }
    public double PitchDegrees { get; }

    /// <summary>WCS geometry/connectivity key, not an approximate-equivalence predicate or persisted schema.</summary>
    public string Signature { get; }

    public RoofSegment3D Segment(RoofTopologyEdge edge) =>
        new(Nodes[edge.StartNodeIndex], Nodes[edge.EndNodeIndex]);

    public IReadOnlyList<RoofPoint3D> BoundaryPoints(RoofTopologyFace face) =>
        Array.AsReadOnly(face.BoundaryNodeIndices.Select(index => Nodes[index]).ToArray());

    private string BuildSignature()
    {
        static string Format(double value, int decimals)
        {
            var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            return (rounded == 0d ? 0d : rounded).ToString("R", CultureInfo.InvariantCulture);
        }
        var values = new List<string> { "RoofTopology", BoundaryVertexCount.ToString(CultureInfo.InvariantCulture), Format(PitchDegrees, 10) };
        values.AddRange(Nodes.Select(point => string.Join(",",
            Format(point.X, 6), Format(point.Y, 6), Format(point.Z, 6))));
        values.Add("Edges");
        values.AddRange(Edges.Select(edge => string.Join(",",
            edge.StartNodeIndex.ToString(CultureInfo.InvariantCulture),
            edge.EndNodeIndex.ToString(CultureInfo.InvariantCulture), edge.Kind.ToString(),
            string.Join(",", edge.FaceIndices.Select(index => index.ToString(CultureInfo.InvariantCulture))))));
        values.Add("Faces");
        values.AddRange(Faces.Select(face => string.Join(",",
            face.BoundaryNodeIndices.Select(index => index.ToString(CultureInfo.InvariantCulture)))));
        return string.Join(";", values);
    }
}
