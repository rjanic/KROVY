using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Complete-face coverage: station lattice must extend over the full face projection,
/// not stop at Ridge-family/component extent. Live Solve and direct Create must agree.
/// </summary>
public sealed class RoofFaceRafterCompleteFaceCoverageTests
{
    [Fact]
    public void ExpandedFace_EaveProjectionBeyondRidgeComponent_CoversToHip()
    {
        var compact = OffsetParallelRidgeFootprint();
        var expanded = ExpandRightEave(compact, extraMm: 8000d);
        var topology = Solve(expanded, 35d);
        var layout = Create(topology, 900d);
        var reports = RoofFaceRafterLayoutService.EvaluateFaceCoverage(topology, layout);

        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(topology, layout));
        Assert.All(reports, report =>
            Assert.Equal(RoofFaceRafterFaceCoverageResult.Pass, report.Result));

        var wideFace = reports
            .OrderByDescending(report => report.FullFaceMaxT - report.FullFaceMinT)
            .First();
        Assert.True(wideFace.FullFaceMaxT - wideFace.FullFaceMinT >
            (wideFace.RidgeFamilyMaxT ?? wideFace.EnumerationMaxT) -
            (wideFace.RidgeFamilyMinT ?? wideFace.EnumerationMinT) + 500d ||
            wideFace.RidgeFamilyMinT is null);
        Assert.True(wideFace.UncoveredStartMm <= 900d + 1d);
        Assert.True(wideFace.UncoveredEndMm <= 900d + 1d);
        Assert.Contains(layout.Segments.Where(segment =>
                segment.SourceFaceIndex == wideFace.FaceId),
            segment =>
                segment.StartBoundaryRole == RoofRafterBoundaryRole.Hip ||
                segment.EndBoundaryRole == RoofRafterBoundaryRole.Hip);
    }

    [Fact]
    public void ExpandedH5_LastStationsContinueTowardHipWithoutLargeUncoveredEnds()
    {
        var compact = H5IrregularTCreate();
        var expanded = ExpandRightEave(compact, extraMm: 10000d);
        var before = Create(Solve(compact, 35d), 900d);
        var afterTopology = Solve(expanded, 35d);
        var after = Create(afterTopology, 900d);

        Assert.True(after.Segments.Count > before.Segments.Count);
        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(afterTopology, after));
        Assert.All(
            RoofFaceRafterLayoutService.EvaluateFaceCoverage(afterTopology, after),
            report =>
            {
                Assert.Equal(RoofFaceRafterFaceCoverageResult.Pass, report.Result);
                Assert.True(report.UncoveredStartMm <= 900d + 1d);
                Assert.True(report.UncoveredEndMm <= 900d + 1d);
            });
    }

    [Fact]
    public void DirectFinalPolygonSolve_EqualsLiveSolverPath()
    {
        var polygon = ExpandRightEave(H5IrregularTCreate(), 10000d);
        var hip = SolveHip(polygon, 35d);
        var face = Create(hip.Topology, 900d);
        var live = RoofRafterLayoutSolver.Solve(
            hip,
            new RafterLayoutParameters(900d, 50d));
        Assert.True(live.IsValid, live.Error.ToString());
        Assert.NotNull(live.Layout);
        Assert.Equal(face.Segments.Count, live.Layout!.Rafters.Count);
        Assert.Equal(
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(face.Signature),
            RoofGeneratedLayoutFingerprint.ToPersistedIdentity(live.Layout.Signature));
        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(hip.Topology, face));
    }

    [Fact]
    public void SteppedOffsetRidgePhase_RemainsSharedAfterExpansion()
    {
        var topology = Solve(ExpandRightEave(OffsetParallelRidgeFootprint(), 6000d), 35d);
        var layout = Create(topology, 900d);
        var plan = RoofFaceRafterLayoutService.DescribePhasePlan(topology, 900d);
        Assert.Contains(
            plan.Components,
            component => component.CouplingReasons.Contains("shared-face-offset-parallel"));
        var report = RoofFaceRafterLayoutService.EvaluateSharedRidgePairing(topology, layout);
        Assert.True(report.IsSatisfied);
        Assert.Equal(0, report.UnmatchedLeft + report.UnmatchedRight);
        Assert.Equal(0d, report.MaxPairGapMm, 8);
        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(topology, layout));
    }

    [Fact]
    public void ReverseWinding_IdenticalPhysicalSegments()
    {
        var forward = ExpandRightEave(OffsetParallelRidgeFootprint(), 5000d);
        var reverse = forward.Reverse().ToArray();
        var a = Create(Solve(forward, 35d), 900d);
        var b = Create(Solve(reverse, 35d), 900d);
        AssertPhysicalEquivalent(a, b);
    }

    [Fact]
    public void ValidShortHipRafters_Preserved()
    {
        // Compact L already produces short Hip-terminated edge stations; expansion
        // must keep them rather than suppressing by an artificial length floor.
        var topology = Solve(LShape(), 30d);
        var layout = Create(topology, 500d);
        var shortHip = layout.Segments.Where(segment =>
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Hip ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Hip) &&
            segment.PlanLengthMm > RoofFaceRafterLayoutService.CoordinateToleranceMm &&
            segment.PlanLengthMm < 600d).ToArray();
        Assert.NotEmpty(shortHip);
        Assert.DoesNotContain(
            layout.Segments,
            segment => segment.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(topology, layout));
    }

    [Theory]
    [MemberData(nameof(RegressionShapes))]
    public void BaselineShapes_RemainCoveredAndStable(string name, RoofPoint2D[] polygon, int expectedMin)
    {
        var topology = Solve(polygon, 30d);
        var layout = Create(topology, name is "Rectangle" or "Asymmetric" ? 500d : 900d);
        Assert.True(layout.Segments.Count >= expectedMin, name);
        Assert.True(RoofFaceRafterLayoutService.HasCompleteFaceCoverage(topology, layout), name);
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(GeometryKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments
                .Select(segment =>
                    $"{segment.SourceFaceIndex}:{segment.StationIndex}:{segment.StationIntervalIndex}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    public static IEnumerable<object[]> RegressionShapes()
    {
        yield return ["Rectangle", Rectangle(), 64];
        yield return ["L", LShape(), 1];
        yield return ["U", UShape(), 1];
        yield return ["T", TShape(), 1];
        yield return ["Offset", OffsetParallelRidgeFootprint(), 1];
        yield return ["H5", H5IrregularTCreate(), 1];
        yield return ["Asymmetric", AsymmetricTrapezoid(), 1];
    }

    private static void AssertPhysicalEquivalent(
        RoofFaceRafterLayout first,
        RoofFaceRafterLayout second)
    {
        Assert.Equal(first.Segments.Count, second.Segments.Count);
        var a = first.Segments.Select(GeometryKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var b = second.Segments.Select(GeometryKey).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Assert.Equal(a, b);
    }

    private static string GeometryKey(RoofFaceRafterSegment segment)
    {
        static string P(RoofPoint2D point) =>
            Math.Round(point.X, 3, MidpointRounding.AwayFromZero).ToString("R") + "," +
            Math.Round(point.Y, 3, MidpointRounding.AwayFromZero).ToString("R");
        var first = P(segment.PlanStart);
        var second = P(segment.PlanEnd);
        return string.CompareOrdinal(first, second) <= 0 ? first + ";" + second : second + ";" + first;
    }

    private static RoofPoint2D[] ExpandRightEave(RoofPoint2D[] polygon, double extraMm)
    {
        var maxX = polygon.Max(point => point.X);
        return polygon
            .Select(point => Math.Abs(point.X - maxX) <= 1e-6
                ? new RoofPoint2D(point.X + extraMm, point.Y)
                : point)
            .ToArray();
    }

    private static RoofFaceRafterLayout Create(RoofTopology topology, double spacing)
    {
        var result = RoofFaceRafterLayoutService.Create(topology, spacing);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static RoofTopology Solve(RoofPoint2D[] polygon, double slope)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(polygon, true), slope);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Topology!;
    }

    private static HipRoofGeometry SolveHip(RoofPoint2D[] polygon, double slope)
    {
        var footprint = RoofFootprintValidator.Validate(
            new RoofFootprintInput(polygon, true));
        Assert.True(footprint.IsValid, footprint.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(solved.Geometry);
    }

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

    private static RoofPoint2D[] AsymmetricTrapezoid() =>
        [new(0, 0), new(12000, 0), new(10000, 6000), new(2000, 6000)];
}
