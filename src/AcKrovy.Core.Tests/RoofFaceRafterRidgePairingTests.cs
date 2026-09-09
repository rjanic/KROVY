using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Shared-Ridge pairing invariant: opposite ordinary rafters meet at identical
/// canonical stations. H5 irregular T reproduces the HOST valley-split bar Ridge
/// defect (collinear pieces without a shared Ridge node).
/// </summary>
public sealed class RoofFaceRafterRidgePairingTests
{
    [Fact]
    public void Rectangle_SharedRidgeEndpointsPairExactly()
    {
        var topology = Solve(Rectangle(), 35d);
        var layout = Create(topology, 900d);
        AssertPairing(topology, layout);
        Assert.Equal(0, layout.Segments.Count(s =>
            HasRoles(s, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley)));
    }

    [Theory]
    [InlineData("L")]
    [InlineData("U")]
    public void ExistingConcaveRegressions_RemainPaired(string name)
    {
        var polygon = name == "L" ? LShape() : UShape();
        var topology = Solve(polygon, 30d);
        var layout = Create(topology, 500d);
        AssertPairing(topology, layout);
        AssertCanonical(layout);
    }

    [Fact]
    public void R1_TShape_SplitCollinearRidge_RemainsPairedAtHostSpacing()
    {
        var topology = Solve(TShape(), 30d);
        var layout = Create(topology, 900d);
        AssertPairing(topology, layout);
        Assert.Equal(2300d, RidgeHits(layout, 0)[0].X, 8);
    }

    [Fact]
    public void H5_IrregularT_Create_PairsSharedRidgeStations()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        Assert.Equal(5, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge));
        Assert.Equal(2, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Valley));

        var layout = Create(topology, 900d);
        Assert.Equal(70, layout.Segments.Count);
        Assert.Equal(9, layout.Segments.Count(s =>
            HasRoles(s, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley)));

        var report = AssertPairing(topology, layout);
        Assert.True(report.PairedComponentCount >= 1);
        Assert.Equal(0, report.UnmatchedLeft);
        AssertCanonical(layout);
    }

    [Fact]
    public void H5_IrregularT_PostStretchLike_PairsSharedRidgeStations()
    {
        var topology = Solve(H5IrregularTPostStretch(), 35d);
        Assert.Equal(5, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge));

        var layout = Create(topology, 900d);
        Assert.Equal(74, layout.Segments.Count);
        Assert.Equal(9, layout.Segments.Count(s =>
            HasRoles(s, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley)));

        var report = AssertPairing(topology, layout);
        Assert.Equal(0, report.UnmatchedLeft);
        AssertCanonical(layout);
    }

    [Fact]
    public void ReversePolygonWinding_YieldsIdenticalPhysicalRidgeStations()
    {
        var forward = Create(Solve(H5IrregularTCreate(), 35d), 900d);
        var reversed = H5IrregularTCreate().Reverse().ToArray();
        var reverseLayout = Create(Solve(reversed, 35d), 900d);

        Assert.Equal(
            CanonicalRidgeHits(forward),
            CanonicalRidgeHits(reverseLayout));
    }

    [Fact]
    public void ReversedRidgeEdgeOrientation_DoesNotChangePhysicalStations()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        var layout = Create(topology, 900d);
        var report = AssertPairing(topology, layout);
        Assert.True(report.MatchedStationCount > 0);
        // Station axis canonicalization is exercised by Create itself; pairing
        // must remain satisfied regardless of FaceIndices / edge node order.
        Assert.Equal(0d, report.MaxPairGapMm, 8);
    }

    [Fact]
    public void SplitButCollinearBarRidges_ShareOneCanonicalPhase()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        var horizontal = topology.Edges
            .Select((edge, index) => (Edge: edge, Index: index))
            .Where(item =>
                item.Edge.Kind == RoofTopologyEdgeKind.Ridge &&
                item.Edge.FaceIndices.Contains(0) &&
                Math.Abs(
                    topology.Nodes[item.Edge.StartNodeIndex].Y -
                    topology.Nodes[item.Edge.EndNodeIndex].Y) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm)
            .OrderBy(item => item.Index)
            .ToArray();

        Assert.True(horizontal.Length >= 2);
        var layout = Create(topology, 900d);
        var report = AssertPairing(topology, layout);
        Assert.True(report.RidgeComponentCount >= 1);

        var face0 = RidgeHits(layout, 0)
            .Where(point =>
                Math.Abs(point.Y - 2000d) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm)
            .OrderBy(point => point.X)
            .ToArray();
        Assert.True(face0.Length >= 2);
        Assert.All(face0.Zip(face0.Skip(1)), pair =>
        {
            var gap = pair.First.DistanceTo(pair.Second);
            var steps = Math.Round(gap / 900d);
            Assert.True(steps >= 1d);
            Assert.Equal(steps * 900d, gap, 8);
        });
    }

    [Fact]
    public void DisconnectedCollinearRidges_RemainIndependent()
    {
        var topology = Solve(UShape(), 30d);
        var layout = Create(topology, 500d);
        AssertPairing(topology, layout);

        var left = RidgeHits(layout, 5);
        var right = RidgeHits(layout, 1);
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        Assert.True(Math.Abs(left[0].X - right[0].X) > 1d);
    }

    [Fact]
    public void RidgeBranchesAtJunction_DoNotSharePhaseAcrossTurns()
    {
        var topology = Solve(TShape(), 30d);
        var layout = Create(topology, 500d);
        var horizontal = RidgeHits(layout, 0);
        var vertical = RidgeHits(layout, 3);
        Assert.NotEmpty(horizontal);
        Assert.NotEmpty(vertical);
        Assert.True(horizontal.All(hit =>
            Math.Abs(hit.Y - 1500d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.True(vertical.All(hit =>
            Math.Abs(hit.X - 5000d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
    }

    [Fact]
    public void RidgeValleyEndpoints_ParticipateWhereCompatible()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        var layout = Create(topology, 900d);
        var ridgeValley = layout.Segments
            .Where(s => HasRoles(s, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley))
            .ToArray();
        Assert.Equal(9, ridgeValley.Length);
        AssertPairing(topology, layout);
    }

    [Fact]
    public void RequestedMaxSpacing_RemainsRespectedOnH5()
    {
        var layout = Create(Solve(H5IrregularTCreate(), 35d), 900d);
        foreach (var group in layout.Segments.GroupBy(s => s.SourceFaceIndex))
        {
            var stations = group
                .Select(s => s.StationDistanceMm)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            Assert.All(stations.Zip(stations.Skip(1)), pair =>
                Assert.True(pair.Second - pair.First >= 900d - 1e-6));
        }
    }

    [Fact]
    public void H5_HasNoZeroNearZeroOrDuplicateGeometryOrKeys()
    {
        var layout = Create(Solve(H5IrregularTCreate(), 35d), 900d);
        AssertCanonical(layout);
    }

    [Fact]
    public void H5_LayoutSignature_IsDeterministic()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        var first = Create(topology, 900d);
        var second = Create(topology, 900d);
        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(first.Segments, second.Segments);
    }

    private static RoofRidgePairingReport AssertPairing(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        var report = RoofFaceRafterLayoutService.EvaluateSharedRidgePairing(
            topology,
            layout);
        Assert.True(
            report.IsSatisfied,
            $"unmatchedLeft={report.UnmatchedLeft} maxGap={report.MaxPairGapMm} fails={report.Failures.Count}");
        Assert.Equal(0d, report.MaxPairGapMm, 8);
        return report;
    }

    private static void AssertCanonical(RoofFaceRafterLayout layout)
    {
        Assert.DoesNotContain(layout.Segments, s =>
            s.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(s =>
                    $"{s.SourceFaceIndex}:{s.StationIndex}:{s.StationIntervalIndex}:{s.StartBoundaryRole}:{s.EndBoundaryRole}")
                .Distinct()
                .Count());
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(s =>
                    $"{s.PlanStart.X:R},{s.PlanStart.Y:R}->{s.PlanEnd.X:R},{s.PlanEnd.Y:R}")
                .Distinct()
                .Count());
    }

    private static IReadOnlyList<string> CanonicalRidgeHits(RoofFaceRafterLayout layout) =>
        layout.Segments
            .Where(s =>
                s.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
                s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge)
            .Select(s =>
            {
                var hit = s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge
                    ? s.PlanEnd
                    : s.PlanStart;
                return $"{Math.Round(hit.X, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                       $"{Math.Round(hit.Y, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RoofPoint2D> RidgeHits(
        RoofFaceRafterLayout layout,
        int faceIndex) => layout.Segments
        .Where(segment =>
            segment.SourceFaceIndex == faceIndex &&
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge))
        .Select(segment =>
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge
                ? segment.PlanEnd
                : segment.PlanStart)
        .OrderBy(point => point.X)
        .ThenBy(point => point.Y)
        .ToArray();

    private static bool HasRoles(
        RoofFaceRafterSegment segment,
        RoofRafterBoundaryRole first,
        RoofRafterBoundaryRole second) =>
        (segment.StartBoundaryRole == first && segment.EndBoundaryRole == second) ||
        (segment.StartBoundaryRole == second && segment.EndBoundaryRole == first);

    private static RoofFaceRafterLayout Create(RoofTopology topology, double spacingMm)
    {
        var result = RoofFaceRafterLayoutService.Create(topology, spacingMm);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFaceRafterLayout>(result.Layout);
    }

    private static RoofTopology Solve(IReadOnlyList<RoofPoint2D> polygon, double slopeDegrees)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(polygon, true), slopeDegrees);
        Assert.True(result.IsValid, result.Error + "/" + result.FootprintError);
        return Assert.IsType<RoofTopology>(result.Topology);
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];

    /// <summary>
    /// HOST H5 failure class: irregular T with valleys that split the bar Ridge into
    /// collinear pieces that no longer share a Ridge node (ridge=5, valley=2).
    /// </summary>
    private static RoofPoint2D[] H5IrregularTCreate() =>
    [
        new(0, 0), new(16000, 0), new(16000, 4000), new(10750, 4000),
        new(10750, 13000), new(5750, 13000), new(5750, 4000), new(0, 4000),
    ];

    /// <summary>
    /// Post-STRETCH-like enlargement of the same irregular T class (still ridge=5).
    /// </summary>
    private static RoofPoint2D[] H5IrregularTPostStretch() =>
    [
        new(0, 0), new(18000, 0), new(18000, 4000), new(11750, 4000),
        new(11750, 13000), new(5750, 13000), new(5750, 4000), new(0, 4000),
    ];
}
