using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Stepped / offset-parallel Ridge phase coupling: one ordinary face with multiple
/// compatible Ridge boundaries must share one canonical station lattice even when
/// those Ridges are perpendicularly offset.
/// </summary>
public sealed class RoofFaceRafterOffsetParallelRidgeTests
{
    [Fact]
    public void OffsetParallelRidge_Create_PairsSharedRidgeStations()
    {
        var topology = Solve(OffsetParallelRidgeFootprint(), 35d);
        var layout = Create(topology, 900d);
        var report = AssertPairing(topology, layout);
        Assert.True(report.MatchedStationCount > 0);
        Assert.Equal(0d, report.MaxPairGapMm, 8);
        AssertCanonical(layout);
    }

    [Fact]
    public void OffsetParallelRidge_DirectCreateEqualsResolveOfSameFinalPolygon()
    {
        var polygon = OffsetParallelRidgeFootprint();
        var first = Create(Solve(polygon, 35d), 900d);
        var second = Create(Solve(polygon, 35d), 900d);
        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(first.Segments, second.Segments);
    }

    [Fact]
    public void OffsetParallelRidge_SharedFaceCouplesCompatibleRidgeBoundaries()
    {
        var topology = Solve(OffsetParallelRidgeFootprint(), 35d);
        var plan = RoofFaceRafterLayoutService.DescribePhasePlan(topology, 900d);
        var offsetFamily = plan.Components.Single(component =>
            component.CouplingReasons.Contains("shared-face-offset-parallel"));

        Assert.False(offsetFamily.MembersCollinear);
        Assert.True(offsetFamily.OffsetDistanceMm > 1d);
        Assert.True(offsetFamily.RidgeEdgeIds.Count >= 2);
        Assert.Contains(0, offsetFamily.IncidentFaceIds);
        Assert.Contains(0, offsetFamily.FacesConsumingPhase);
    }

    [Fact]
    public void OffsetParallelRidge_FacePhaseFallbackAbsentForCoupledFamily()
    {
        var topology = Solve(OffsetParallelRidgeFootprint(), 35d);
        var plan = RoofFaceRafterLayoutService.DescribePhasePlan(topology, 900d);
        var face0 = plan.FaceDecisions.Single(decision => decision.FaceId == 0);

        Assert.True(face0.RidgeBoundaryIds.Count >= 2);
        Assert.False(face0.FallbackUsed);
        Assert.Equal("ridge-family", face0.FallbackReason);
        Assert.NotNull(face0.SelectedPhaseT);
    }

    [Fact]
    public void UnrelatedParallelRidges_RemainIndependentComponents()
    {
        var topology = Solve(UShape(), 30d);
        var plan = RoofFaceRafterLayoutService.DescribePhasePlan(topology, 500d);
        Assert.DoesNotContain(
            plan.Components,
            component => component.CouplingReasons.Contains("shared-face-offset-parallel"));
        Assert.True(plan.Components.Count >= 2);
        Assert.All(
            plan.Components.Where(component => component.RidgeEdgeIds.Count == 1),
            component => Assert.Contains("singleton", component.CouplingReasons));

        var layout = Create(topology, 500d);
        AssertPairing(topology, layout);
        var left = RidgeHits(layout, 5);
        var right = RidgeHits(layout, 1);
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        Assert.True(Math.Abs(left[0].X - right[0].X) > 1d);
    }

    [Fact]
    public void CollinearSplitRidge_RegressionStillPairs()
    {
        var topology = Solve(H5IrregularTCreate(), 35d);
        var layout = Create(topology, 900d);
        AssertPairing(topology, layout);
        var plan = RoofFaceRafterLayoutService.DescribePhasePlan(topology, 900d);
        Assert.Contains(
            plan.Components,
            component =>
                component.CouplingReasons.Contains("shared-face-collinear") ||
                component.RidgeEdgeIds.Count >= 2);
    }

    [Fact]
    public void OffsetParallelRidge_ReverseWinding_IdenticalPhysicalLayout()
    {
        var forward = Create(Solve(OffsetParallelRidgeFootprint(), 35d), 900d);
        var reversed = OffsetParallelRidgeFootprint().Reverse().ToArray();
        var reverseLayout = Create(Solve(reversed, 35d), 900d);
        Assert.Equal(CanonicalRidgeHits(forward), CanonicalRidgeHits(reverseLayout));
    }

    [Fact]
    public void OffsetParallelRidge_RidgeValleyPreserved()
    {
        var layout = Create(Solve(OffsetParallelRidgeFootprint(), 35d), 900d);
        Assert.Contains(layout.Segments, segment =>
            HasRoles(segment, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley));
        AssertCanonical(layout);
    }

    [Fact]
    public void OffsetParallelRidge_AdapterPreservesPairedEndpoints()
    {
        var geometry = SolveHip(OffsetParallelRidgeFootprint(), 35d);
        var faceLayout = Create(geometry.Topology, 900d);
        AssertPairing(geometry.Topology, faceLayout);

        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry,
            faceLayout,
            80d,
            out var adapted));
        Assert.Equal(faceLayout.Segments.Count, adapted.Rafters.Count);
        Assert.All(faceLayout.Segments.Zip(adapted.Rafters), pair =>
        {
            Assert.Equal(pair.First.PlanStart.X, pair.Second.PlanStart.X, 8);
            Assert.Equal(pair.First.PlanStart.Y, pair.Second.PlanStart.Y, 8);
            Assert.Equal(pair.First.PlanEnd.X, pair.Second.PlanEnd.X, 8);
            Assert.Equal(pair.First.PlanEnd.Y, pair.Second.PlanEnd.Y, 8);
        });
    }

    [Fact]
    public void OffsetParallelRidge_MaxSpacingRespected()
    {
        const double spacing = 900d;
        var layout = Create(Solve(OffsetParallelRidgeFootprint(), 35d), spacing);
        foreach (var group in layout.Segments.GroupBy(segment => segment.SourceFaceIndex))
        {
            var stations = group
                .Select(segment => segment.StationDistanceMm)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            Assert.All(stations.Zip(stations.Skip(1)), pair =>
                Assert.True(pair.Second - pair.First + 1e-9 >= spacing));
        }
    }

    [Fact]
    public void OffsetParallelRidge_NoDuplicatesOrNearZero()
    {
        AssertCanonical(Create(Solve(OffsetParallelRidgeFootprint(), 35d), 900d));
    }

    [Theory]
    [InlineData("Rectangle")]
    [InlineData("L")]
    [InlineData("U")]
    [InlineData("T")]
    public void ExistingShapes_RemainPaired(string name)
    {
        var polygon = name switch
        {
            "Rectangle" => Rectangle(),
            "L" => LShape(),
            "U" => UShape(),
            _ => TShape(),
        };
        var slope = name is "Rectangle" or "T" ? 30d : 30d;
        var spacing = name == "Rectangle" ? 900d : name == "T" ? 900d : 500d;
        var topology = Solve(polygon, slope);
        AssertPairing(topology, Create(topology, spacing));
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
            $"unmatchedLeft={report.UnmatchedLeft} maxGap={report.MaxPairGapMm}");
        Assert.Equal(0d, report.MaxPairGapMm, 8);
        return report;
    }

    private static void AssertCanonical(RoofFaceRafterLayout layout)
    {
        Assert.DoesNotContain(layout.Segments, segment =>
            segment.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(segment =>
                    $"{segment.SourceFaceIndex}:{segment.StationIndex}:{segment.StationIntervalIndex}:{segment.StartBoundaryRole}:{segment.EndBoundaryRole}")
                .Distinct()
                .Count());
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(segment =>
                    $"{segment.PlanStart.X:R},{segment.PlanStart.Y:R}->{segment.PlanEnd.X:R},{segment.PlanEnd.Y:R}")
                .Distinct()
                .Count());
    }

    private static IReadOnlyList<string> CanonicalRidgeHits(RoofFaceRafterLayout layout) =>
        layout.Segments
            .Where(segment =>
                segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
                segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge)
            .Select(segment =>
            {
                var hit = segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge
                    ? segment.PlanEnd
                    : segment.PlanStart;
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

    private static HipRoofGeometry SolveHip(IReadOnlyList<RoofPoint2D> polygon, double slopeDegrees)
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(polygon, true));
        Assert.True(footprint.IsValid, footprint.Error.ToString());
        var solved = RoofGeometrySolver.Solve(
            new RoofDefinition(
                footprint.Footprint!,
                new RoofParameters(slopeDegrees),
                RoofKind.Hip));
        return Assert.IsType<HipRoofGeometry>(solved.Geometry);
    }

    /// <summary>
    /// Concave footprint whose Hip topology yields two long parallel Ridge segments
    /// offset perpendicular to their direction and linked by junction diagonals.
    /// </summary>
    private static RoofPoint2D[] OffsetParallelRidgeFootprint() =>
    [
        new(0, 0), new(16000, 0), new(16000, 5000), new(10000, 5000),
        new(10000, 12000), new(5000, 12000), new(5000, 3000), new(0, 3000),
    ];

    private static RoofPoint2D[] H5IrregularTCreate() =>
    [
        new(0, 0), new(16000, 0), new(16000, 4000), new(10750, 4000),
        new(10750, 13000), new(5750, 13000), new(5750, 4000), new(0, 4000),
    ];

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
}
