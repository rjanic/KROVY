namespace AcKrovy.Core.Models.Roofs;

/// <summary>Hip presentation view of one shared topology face; stores no duplicate geometry.</summary>
public sealed class HipRoofFace
{
    private readonly RoofTopology _topology;
    private readonly RoofTopologyFace _face;

    internal HipRoofFace(int index, RoofTopology topology, RoofTopologyFace face)
    {
        Index = index;
        _topology = topology;
        _face = face;
    }

    /// <summary>Presentation order; rectangles retain negative-transverse-first ordering.</summary>
    public int Index { get; }
    public int SourceEdgeIndex => _face.SourceEdgeIndex;
    public IReadOnlyList<RoofPoint3D> BoundaryPoints => _topology.BoundaryPoints(_face);
    public RoofSegment3D Eave => _topology.Segment(_topology.Edges[SourceEdgeIndex]);

    /// <summary>Actual geometric pitch, including the tolerance-sized adjustment at near-square collapse.</summary>
    public double SlopeDegrees
    {
        get
        {
            var a = _topology.Nodes[_face.BoundaryNodeIndices[0]];
            var b = _topology.Nodes[_face.BoundaryNodeIndices[1]];
            var c = _topology.Nodes[_face.BoundaryNodeIndices[2]];
            var run = ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / Eave.LengthMm;
            return Math.Atan2(c.Z - a.Z, run) * 180d / Math.PI;
        }
    }
}
