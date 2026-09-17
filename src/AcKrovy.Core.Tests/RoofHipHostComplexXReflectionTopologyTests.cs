using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;
using Xunit.Abstractions;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Exact HOST Complex-X footprint reproduction for whole-roof MIRROR reflection
/// topology asymmetry. Uses the production topology + structural resolver path.
/// </summary>
public sealed class RoofHipHostComplexXReflectionTopologyTests
{
    private readonly ITestOutputHelper _output;

    public RoofHipHostComplexXReflectionTopologyTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void HostComplexX_SourceMatchesKnownStructuralInputCounts()
    {
        var source = SolveProduction(HostComplexX());
        Assert.Equal(7, source.RidgeTopology);
        Assert.Equal(10, source.HipTopology);
        Assert.Equal(4, source.ValleyTopology);
        Assert.Equal(12, source.EligibleHip);
        Assert.Equal(4, source.EligibleValley);
        Assert.Equal(12, source.DesiredHip);
        Assert.Equal(4, source.DesiredValley);
    }

    [Fact]
    public void HostComplexX_MirrorAcrossHorizontal_IsReflectionInvariant()
    {
        var source = SolveProduction(HostComplexX());
        var mirroredVertices = MirrorAcrossHorizontalThroughCentroid(HostComplexX());
        var mirrored = SolveProduction(mirroredVertices);

        Dump("SOURCE", source);
        Dump("MIRROR", mirrored);
        DumpEdges("SOURCE", source);
        DumpEdges("MIRROR", mirrored);

        Assert.Equal(7, source.RidgeTopology);
        Assert.Equal(10, source.HipTopology);
        Assert.Equal(4, source.ValleyTopology);
        Assert.Equal(12, source.EligibleHip);
        Assert.Equal(4, source.EligibleValley);
        Assert.Equal(12, source.DesiredHip);
        Assert.Equal(4, source.DesiredValley);

        Assert.Equal(source.RidgeTopology, mirrored.RidgeTopology);
        Assert.Equal(source.HipTopology, mirrored.HipTopology);
        Assert.Equal(source.ValleyTopology, mirrored.ValleyTopology);
        Assert.Equal(source.EligibleHip, mirrored.EligibleHip);
        Assert.Equal(source.EligibleValley, mirrored.EligibleValley);
        Assert.Equal(source.DesiredHip, mirrored.DesiredHip);
        Assert.Equal(source.DesiredValley, mirrored.DesiredValley);
    }

    [Fact]
    public void HostComplexX_MirrorAcrossXAxis_IsReflectionInvariant()
    {
        var source = SolveProduction(HostComplexX());
        var mirrored = SolveProduction(MirrorAcrossXAxis(HostComplexX()));
        Dump("SOURCE_X", source);
        Dump("MIRROR_X", mirrored);
        Assert.Equal(7, source.RidgeTopology);
        Assert.Equal(10, source.HipTopology);
        Assert.Equal(source.RidgeTopology, mirrored.RidgeTopology);
        Assert.Equal(source.HipTopology, mirrored.HipTopology);
        Assert.Equal(source.EligibleHip, mirrored.EligibleHip);
        Assert.Equal(source.DesiredHip, mirrored.DesiredHip);
    }

    private void Dump(string label, SolvedSnapshot snap)
    {
        _output.WriteLine(
            $"{label} ridge={snap.RidgeTopology} hip={snap.HipTopology} valley={snap.ValleyTopology} " +
            $"eligibleHip={snap.EligibleHip} eligibleValley={snap.EligibleValley} " +
            $"desiredHip={snap.DesiredHip} desiredValley={snap.DesiredValley} " +
            $"orientation={snap.Orientation} start=({snap.FirstVertex.X},{snap.FirstVertex.Y})");
    }

    private void DumpEdges(string label, SolvedSnapshot snap)
    {
        var topology = snap.Geometry.Topology;
        for (var i = 0; i < topology.Edges.Count; i++)
        {
            var edge = topology.Edges[i];
            if (edge.Kind is RoofTopologyEdgeKind.Eave or RoofTopologyEdgeKind.CoplanarSeam)
            {
                continue;
            }

            var segment = topology.Segment(edge);
            _output.WriteLine(
                $"{label} edge[{i}] kind={edge.Kind} v={edge.StartNodeIndex}->{edge.EndNodeIndex} " +
                $"origin={edge.OriginatingBoundaryVertexIndex?.ToString() ?? "-"} " +
                $"faces=[{string.Join(',', edge.FaceIndices)}] " +
                $"len={segment.LengthMm:F3} " +
                $"dz={Math.Abs(segment.Start.Z - segment.End.Z):F3} " +
                $"a=({segment.Start.X:F3},{segment.Start.Y:F3},{segment.Start.Z:F3}) " +
                $"b=({segment.End.X:F3},{segment.End.Y:F3},{segment.End.Z:F3})");
        }

        foreach (var edge in snap.Resolution.Edges.OrderBy(e => e.TopologyEdgeIndex))
        {
            if (edge.StructuralRole is not (RoofStructuralRole.Hip or RoofStructuralRole.Valley or RoofStructuralRole.Ridge))
            {
                continue;
            }

            _output.WriteLine(
                $"{label} role[{edge.TopologyEdgeIndex}] {edge.StructuralRole} " +
                $"key={edge.StructuralIdentity} eligible={edge.IsAutomaticStructuralTimberEligible} " +
                $"origin={edge.OriginatingBoundaryVertexIndex?.ToString() ?? "-"} " +
                $"pathAnchor={edge.PhysicalPathAnchorVertexIndex?.ToString() ?? "-"}");
        }
    }

    private static SolvedSnapshot SolveProduction(RoofPoint2D[] vertices)
    {
        var input = new RoofFootprintInput(vertices, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);

        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid, provenance.IdentityError.ToString());
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var structural = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(structural.IsValid, structural.Error.ToString());

        return new SolvedSnapshot(
            geometry,
            resolution,
            normalized.Validation.SourceOrientation,
            vertices[0],
            geometry.Topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge),
            geometry.Topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Hip),
            geometry.Topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Valley),
            resolution.Edges.Count(e =>
                e.StructuralRole == RoofStructuralRole.Hip &&
                e.IsAutomaticStructuralTimberEligible),
            resolution.Edges.Count(e =>
                e.StructuralRole == RoofStructuralRole.Valley &&
                e.IsAutomaticStructuralTimberEligible),
            structural.Items.Count(i => i.ElementType == TimberElementType.HipRafter),
            structural.Items.Count(i => i.ElementType == TimberElementType.ValleyRafter));
    }

    /// <summary>
    /// AutoCAD MIRROR across a horizontal axis: Y' = 2*axisY - Y.
    /// Axis through footprint centroid keeps the assembly in place roughly like a
    /// typical interactive mirror of the whole roof.
    /// </summary>
    private static RoofPoint2D[] MirrorAcrossHorizontalThroughCentroid(RoofPoint2D[] source)
    {
        var axisY = source.Average(p => p.Y);
        return source.Select(p => new RoofPoint2D(p.X, 2d * axisY - p.Y)).ToArray();
    }

    private static RoofPoint2D[] MirrorAcrossXAxis(RoofPoint2D[] source) =>
        source.Select(p => new RoofPoint2D(p.X, -p.Y)).ToArray();

    // Exact HOST source polygon (owner 2912), closed back to vertex 0.
    private static RoofPoint2D[] HostComplexX() =>
    [
        new(39543.03572550637, 17861.178862711335),
        new(39543.03572550637, 14904.255448377728),
        new(42773.215027821076, 14904.255448377728),
        new(42773.215027821076, 11621.799274236655),
        new(47523.47865744759, 11621.799274236655),
        new(47523.47865744759, 15202.660605693112),
        new(50997.957058859145, 15202.660605693112),
        new(50997.957058859145, 18620.75548770483),
        new(46872.013823711604, 18620.75548770483),
        new(46872.013823711604, 22472.89416536154),
        new(43370.39098304298, 22472.89416536154),
        new(43370.39098304298, 17861.178862711335),
    ];

    private sealed record SolvedSnapshot(
        HipRoofGeometry Geometry,
        RoofStructuralEdgeResolutionResult Resolution,
        RoofPolygonOrientation Orientation,
        RoofPoint2D FirstVertex,
        int RidgeTopology,
        int HipTopology,
        int ValleyTopology,
        int EligibleHip,
        int EligibleValley,
        int DesiredHip,
        int DesiredValley);
}
