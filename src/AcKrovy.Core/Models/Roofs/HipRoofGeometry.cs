namespace AcKrovy.Core.Models.Roofs;

/// <summary>Hip preset view over shared polygon topology; no fixed ridge or face count.</summary>
public sealed class HipRoofGeometry : IRoofGeometry
{
    private readonly int _firstFace;

    internal HipRoofGeometry(RoofTopology topology, RoofDirection2D orientationDirection, int firstFace)
    {
        Topology = topology;
        OrientationDirection = orientationDirection;
        _firstFace = firstFace;
        Faces = Array.AsReadOnly(Enumerable.Range(0, topology.Faces.Count).Select(index =>
            new HipRoofFace(index, topology, topology.Faces[(firstFace + index) % topology.Faces.Count])).ToArray());
    }

    public RoofTopology Topology { get; }
    public RoofKind Kind => RoofKind.Hip;
    public IReadOnlyList<HipRoofFace> Faces { get; }
    public IReadOnlyList<RoofSegment3D> Ridges => Segments(RoofTopologyEdgeKind.Ridge);
    public IReadOnlyList<RoofSegment3D> Valleys => Segments(RoofTopologyEdgeKind.Valley);

    /// <summary>
    /// Hip-classified topology segments. Concave event continuations can retain
    /// convex lineage after leaving the boundary; physical timber eligibility is
    /// resolved separately.
    /// </summary>
    public IReadOnlyList<RoofSegment3D> Hips => Array.AsReadOnly(Topology.Edges
        .Where(edge => edge.Kind == RoofTopologyEdgeKind.Hip)
        .OrderBy(edge => (edge.StartNodeIndex - _firstFace + Faces.Count) % Faces.Count)
        .Select(Topology.Segment).ToArray());

    /// <summary>Convenience for a single-ridge roof; null for zero or multiple ridges.</summary>
    public RoofSegment3D? Ridge
    {
        get
        {
            var ridges = Ridges;
            if (ridges.Count != 1) return null;
            var ridge = ridges[0];
            return (ridge.End.X - ridge.Start.X) * OrientationDirection.X +
                   (ridge.End.Y - ridge.Start.Y) * OrientationDirection.Y >= 0d
                ? ridge : new RoofSegment3D(ridge.End, ridge.Start);
        }
    }

    public RoofPoint3D? Apex => Topology.Nodes.Count == Topology.BoundaryVertexCount + 1
        ? Topology.Nodes[Topology.BoundaryVertexCount] : null;
    public bool IsPyramidal => Apex.HasValue;
    public double RidgeLengthMm => Ridges.Sum(ridge => ridge.LengthMm);
    public double RiseMm => Topology.Nodes.Max(point => point.Z);

    /// <summary>
    /// Legacy IRoofGeometry presentation axis: rectangle ridge axis, otherwise first
    /// canonical eave axis. It is not an input to general topology or a branch direction.
    /// </summary>
    public RoofDirection2D OrientationDirection { get; }
    public double PrimarySlopeDegrees => Topology.PitchDegrees;
    public string Signature => "Hip;" + Topology.Signature;

    private IReadOnlyList<RoofSegment3D> Segments(RoofTopologyEdgeKind kind) =>
        Array.AsReadOnly(Topology.Edges.Where(edge => edge.Kind == kind).Select(Topology.Segment).ToArray());
}
