using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterGeometryTests
{
    private const double MaximumSpacingMm = 1000d;
    private const double RafterPlanWidthMm = 100d;

    [Theory]
    [InlineData(25d)]
    [InlineData(30d)]
    public void RectangularPlane_UsesSharedSpacingAndClipsLowToHigh(double slopeDegrees)
    {
        var geometry = SolveMonopitch(Rectangle(10000d, 6000d), slopeDegrees, Direction(0d, 1d));
        var layout = Layout(geometry);

        var plane = Assert.Single(layout.Planes);
        Assert.Equal(RafterRoofFace.Face0, plane.Face);
        Assert.Equal(4, plane.BoundaryPoints.Count);
        Assert.Equal(10, layout.IntervalCount);
        Assert.Equal(11, layout.StationCount);
        Assert.Equal(11, layout.Rafters.Count);
        Assert.Equal(9900d, layout.UsableCenterSpanMm, 9);
        Assert.Equal(990d, layout.ActualSpacingMm, 9);
        Assert.Equal(1d, layout.StationDirection.X, 12);
        Assert.Equal(0d, layout.StationDirection.Y, 12);

        var first = layout.Rafters[0];
        var last = layout.Rafters[^1];
        Assert.Equal(new RoofPoint2D(50d, 0d), first.PlanStart);
        Assert.Equal(new RoofPoint2D(50d, 6000d), first.PlanEnd);
        Assert.Equal(new RoofPoint2D(9950d, 0d), last.PlanStart);
        Assert.Equal(new RoofPoint2D(9950d, 6000d), last.PlanEnd);
        Assert.Equal(50d, first.StationPositionMm, 12);
        Assert.Equal(9950d, last.StationPositionMm, 12);
        Assert.All(layout.Rafters, rafter =>
        {
            Assert.Equal(0d, rafter.PlanStart.Y, 9);
            Assert.Equal(6000d, rafter.PlanEnd.Y, 9);
            Assert.Equal(0d, rafter.RunDirection.X, 12);
            Assert.Equal(1d, rafter.RunDirection.Y, 12);
            Assert.Equal(slopeDegrees, rafter.SlopeDegrees);
        });
    }

    [Theory]
    [InlineData(25d)]
    [InlineData(30d)]
    public void TrueLength_IsUnroundedPlanLengthOverCosine(double slopeDegrees)
    {
        var rafter = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            slopeDegrees,
            Direction(0d, 1d))).Rafters[4];

        var expected = 6000d / Math.Cos(slopeDegrees * Math.PI / 180d);
        Assert.Equal(6000d, rafter.PlanLengthMm, 10);
        Assert.Equal(expected, rafter.TrueLengthMm, 10);
        Assert.NotEqual(Math.Round(expected), rafter.TrueLengthMm);
    }

    [Fact]
    public void Mirror_ReversesRunButPreservesPhysicalStationsKeysAndLengths()
    {
        var definition = MonopitchDefinition(
            Validate(Rectangle(10000d, 6000d)),
            30d,
            Direction(0d, 1d));
        var original = Layout(SolveMonopitch(definition));
        var mirrored = Layout(SolveMonopitch(MonopitchRoofDefinitionRules.Mirror(definition)));

        Assert.Equal(original.StationCount, mirrored.StationCount);
        Assert.Equal(original.ActualSpacingMm, mirrored.ActualSpacingMm, 12);
        Assert.Equal(original.StationDirection, mirrored.StationDirection);
        for (var index = 0; index < original.Rafters.Count; index++)
        {
            var before = original.Rafters[index];
            var after = mirrored.Rafters[index];
            Assert.Equal(before.LogicalKey, after.LogicalKey);
            Assert.Equal(before.StationPositionMm, after.StationPositionMm, 12);
            Assert.Equal(before.PlanLengthMm, after.PlanLengthMm, 12);
            Assert.Equal(before.TrueLengthMm, after.TrueLengthMm, 12);
            Assert.True(SameUndirectedSegment(before, after));
            Assert.Equal(-before.RunDirection.X, after.RunDirection.X, 12);
            Assert.Equal(-before.RunDirection.Y, after.RunDirection.Y, 12);
        }
    }

    [Fact]
    public void MirrorTwice_ReproducesCanonicalLayoutAndSignature()
    {
        var originalDefinition = MonopitchDefinition(
            Validate(Rectangle(10000d, 6000d)),
            25d,
            Direction(0d, 1d));
        var twiceDefinition = MonopitchRoofDefinitionRules.Mirror(
            MonopitchRoofDefinitionRules.Mirror(originalDefinition));
        var original = Layout(SolveMonopitch(originalDefinition));
        var twice = Layout(SolveMonopitch(twiceDefinition));

        AssertLayoutsEqual(original, twice);
    }

    [Fact]
    public void ResizeAlongLowHigh_ChangesRunAndTrueLengthButNotStations()
    {
        var beforeGeometry = SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            Direction(0d, 1d));
        var afterGeometry = SolveMonopitch(
            Rectangle(10000d, 8000d),
            30d,
            Direction(0d, 1d));
        var before = Layout(beforeGeometry);
        var after = Layout(afterGeometry);

        Assert.Equal(30d, afterGeometry.SlopeDegrees);
        Assert.Equal(before.StationCount, after.StationCount);
        Assert.Equal(before.ActualSpacingMm, after.ActualSpacingMm, 12);
        Assert.Equal(
            before.Rafters.Select(item => item.LogicalKey),
            after.Rafters.Select(item => item.LogicalKey));
        Assert.All(before.Rafters, item => Assert.Equal(6000d, item.PlanLengthMm, 9));
        Assert.All(after.Rafters, item => Assert.Equal(8000d, item.PlanLengthMm, 9));
        Assert.All(after.Rafters, item => Assert.Equal(
            8000d / Math.Cos(Math.PI / 6d),
            item.TrueLengthMm,
            9));
    }

    [Fact]
    public void ResizePerpendicularToLowHigh_ChangesStationsButNotRunLength()
    {
        var before = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            25d,
            Direction(0d, 1d)));
        var after = Layout(SolveMonopitch(
            Rectangle(14000d, 6000d),
            25d,
            Direction(0d, 1d)));

        Assert.Equal(11, before.StationCount);
        Assert.Equal(15, after.StationCount);
        Assert.True(after.ActualSpacingMm <= MaximumSpacingMm);
        Assert.All(after.Rafters, item => Assert.Equal(6000d, item.PlanLengthMm, 9));
        Assert.All(after.Rafters, item => Assert.Equal(
            before.Rafters[0].TrueLengthMm,
            item.TrueLengthMm,
            9));
    }

    [Fact]
    public void SlopeEdit_PreservesPlanStationsAndKeysButRecomputesTrueLength()
    {
        var before = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            25d,
            Direction(0d, 1d)));
        var after = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            35d,
            Direction(0d, 1d)));

        Assert.Equal(before.StationCount, after.StationCount);
        Assert.Equal(before.ActualSpacingMm, after.ActualSpacingMm, 12);
        for (var index = 0; index < before.Rafters.Count; index++)
        {
            Assert.Equal(before.Rafters[index].LogicalKey, after.Rafters[index].LogicalKey);
            Assert.Equal(before.Rafters[index].PlanStart, after.Rafters[index].PlanStart);
            Assert.Equal(before.Rafters[index].PlanEnd, after.Rafters[index].PlanEnd);
            Assert.Equal(before.Rafters[index].PlanLengthMm, after.Rafters[index].PlanLengthMm, 12);
            Assert.NotEqual(before.Rafters[index].TrueLengthMm, after.Rafters[index].TrueLengthMm);
            Assert.Equal(35d, after.Rafters[index].SlopeDegrees);
        }
    }

    [Fact]
    public void RotatedThirtyDegrees_ResizeAndMirrorRemainLocalAxisDeterministic()
    {
        const double rotationDegrees = 30d;
        var radians = rotationDegrees * Math.PI / 180d;
        var lowToHigh = Direction(-Math.Sin(radians), Math.Cos(radians));
        var beforeDefinition = MonopitchDefinition(
            Validate(RotatedRectangle(10000d, 6000d, rotationDegrees)),
            30d,
            lowToHigh);
        var alongRunDefinition = MonopitchDefinition(
            Validate(RotatedRectangle(10000d, 8000d, rotationDegrees)),
            30d,
            lowToHigh);
        var perpendicularDefinition = MonopitchDefinition(
            Validate(RotatedRectangle(14000d, 6000d, rotationDegrees)),
            30d,
            lowToHigh);

        var before = Layout(SolveMonopitch(beforeDefinition));
        var alongRun = Layout(SolveMonopitch(alongRunDefinition));
        var perpendicular = Layout(SolveMonopitch(perpendicularDefinition));
        var mirrored = Layout(SolveMonopitch(MonopitchRoofDefinitionRules.Mirror(beforeDefinition)));

        Assert.Equal(before.StationCount, alongRun.StationCount);
        Assert.All(alongRun.Rafters, rafter => Assert.Equal(8000d, rafter.PlanLengthMm, 7));
        Assert.True(perpendicular.StationCount > before.StationCount);
        Assert.All(perpendicular.Rafters, rafter => Assert.Equal(6000d, rafter.PlanLengthMm, 7));
        for (var index = 0; index < before.Rafters.Count; index++)
        {
            Assert.Equal(before.Rafters[index].LogicalKey, mirrored.Rafters[index].LogicalKey);
            Assert.True(SameUndirectedSegment(before.Rafters[index], mirrored.Rafters[index]));
            Assert.Equal(-before.Rafters[index].RunDirection.X, mirrored.Rafters[index].RunDirection.X, 10);
            Assert.Equal(-before.Rafters[index].RunDirection.Y, mirrored.Rafters[index].RunDirection.Y, 10);
        }
    }

    [Fact]
    public void ThirtyDegreeRotation_PreservesIntrinsicLayoutAndFollowsLocalAxes()
    {
        const double rotationDegrees = 30d;
        var radians = rotationDegrees * Math.PI / 180d;
        var stationAxis = Direction(Math.Cos(radians), Math.Sin(radians));
        var slopeAxis = Direction(-Math.Sin(radians), Math.Cos(radians));
        var baseline = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            Direction(0d, 1d)));
        var rotated = Layout(SolveMonopitch(
            RotatedRectangle(10000d, 6000d, rotationDegrees),
            30d,
            slopeAxis));

        Assert.Equal(baseline.StationCount, rotated.StationCount);
        Assert.Equal(baseline.ActualSpacingMm, rotated.ActualSpacingMm, 8);
        Assert.Equal(stationAxis.X, rotated.StationDirection.X, 10);
        Assert.Equal(stationAxis.Y, rotated.StationDirection.Y, 10);
        for (var index = 0; index < rotated.Rafters.Count; index++)
        {
            var rafter = rotated.Rafters[index];
            Assert.Equal(baseline.Rafters[index].PlanLengthMm, rafter.PlanLengthMm, 8);
            Assert.Equal(baseline.Rafters[index].TrueLengthMm, rafter.TrueLengthMm, 8);
            Assert.Equal(slopeAxis.X, rafter.RunDirection.X, 10);
            Assert.Equal(slopeAxis.Y, rafter.RunDirection.Y, 10);
            Assert.NotEqual(rafter.PlanStart.X, rafter.PlanEnd.X);
            Assert.NotEqual(rafter.PlanStart.Y, rafter.PlanEnd.Y);
        }
    }

    [Fact]
    public void RepeatedSolve_IsDeterministicForOrderingKeysGeometryAndSignature()
    {
        var geometry = SolveMonopitch(
            RotatedRectangle(10000d, 6000d, 30d),
            25d,
            Direction(-0.5d, Math.Sqrt(3d) / 2d));
        var expected = Layout(geometry, 777d, 80d);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            AssertLayoutsEqual(expected, Layout(geometry, 777d, 80d));
        }
        Assert.Equal(
            expected.Rafters.Select(item => item.LogicalKey),
            expected.Rafters.Select(RoofGeneratedMemberKey.From));
    }

    [Fact]
    public void HighToLowUxConversion_LeavesStage2ARafterLayoutBitEquivalent()
    {
        var canonical = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            Direction(0d, 1d)));
        var fromHighToLowUx = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            MonopitchRoofDirectionPresentationRules.ToCanonicalLowToHigh(
                Direction(0d, -1d))));

        AssertLayoutsEqual(canonical, fromHighToLowUx);
    }

    [Fact]
    public void NeutralLogicalAnchorContext_IndexesMonopitchFace0Stations()
    {
        var layout = Layout(SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            Direction(0d, 1d)));
        var rafter = layout.Rafters[4];
        var context = RoofLogicalGeneratedAnchorContext.FromLayout(
            layout,
            125d,
            [RoofGeneratedMemberOverride.Suppress(rafter.LogicalKey)]);

        var resolved = context.Resolve(rafter.LogicalKey);

        Assert.Equal(layout.Rafters.Count, context.LogicalMemberCount);
        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed, resolved.Kind);
        Assert.Equal(
            RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 125d),
            resolved.Geometry);
    }

    [Fact]
    public void InvalidLayoutInputsAndInvalidMonopitchDefinitionsFailSafely()
    {
        var geometry = SolveMonopitch(
            Rectangle(10000d, 6000d),
            30d,
            Direction(0d, 1d));
        var invalidSpacing = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(0d, RafterPlanWidthMm));
        var invalidWidth = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(MaximumSpacingMm, 10000d));

        Assert.False(invalidSpacing.IsValid);
        Assert.Equal(RoofRafterLayoutError.InvalidMaximumSpacing, invalidSpacing.Error);
        Assert.False(invalidWidth.IsValid);
        Assert.Equal(RoofRafterLayoutError.InvalidRafterPlanWidth, invalidWidth.Error);

        var footprint = Validate(Rectangle(10000d, 6000d));
        Assert.False(RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(0d, SlopeDirection: Direction(0d, 1d)),
            RoofKind.Monopitch)).IsValid);
        Assert.False(RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(30d),
            RoofKind.Monopitch)).IsValid);
        Assert.False(RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0d, 0d), new(10000d, 0d), new(10000d, 0d), new(0d, 0d)],
            true)).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPlaneEngine_IsEquivalentToExistingGableLayout(bool asymmetric)
    {
        var geometry = SolveGable(asymmetric);
        var parameters = new RafterLayoutParameters(900d, 80d);
        var existing = SimpleGableRafterLayoutSolver.Solve(geometry, parameters).Layout!;
        var shared = RoofRafterLayoutSolver.Solve(geometry, parameters).Layout!;

        Assert.Equal(2, shared.Planes.Count);
        Assert.Equal(existing.Signature, shared.Signature);
        Assert.Equal(existing.RidgeLengthMm, shared.StationSpanMm);
        Assert.Equal(existing.UsableCenterSpanMm, shared.UsableCenterSpanMm);
        Assert.Equal(existing.IntervalCount, shared.IntervalCount);
        Assert.Equal(existing.StationCount, shared.StationCount);
        Assert.Equal(existing.ActualSpacingMm, shared.ActualSpacingMm);
        Assert.Equal(existing.Rafters.Count, shared.Rafters.Count);
        for (var index = 0; index < existing.Rafters.Count; index++)
        {
            var before = existing.Rafters[index];
            var after = shared.Rafters[index];
            Assert.Equal(before.Face, after.Face);
            Assert.Equal(before.StationIndex, after.StationIndex);
            Assert.Equal(before.StationCount, after.StationCount);
            Assert.Equal(before.StationFraction, after.StationFraction);
            Assert.Equal(before.PlanStart, after.PlanStart);
            Assert.Equal(before.PlanEnd, after.PlanEnd);
            Assert.Equal(before.SlopeDegrees, after.SlopeDegrees);
            Assert.Equal(RoofGeneratedMemberKey.From(before), after.LogicalKey);
            Assert.Equal(
                before.PlanStart.DistanceTo(before.PlanEnd) /
                    Math.Cos(before.SlopeDegrees * Math.PI / 180d),
                after.TrueLengthMm,
                10);
        }
        for (var station = 0; station < shared.StationCount; station++)
        {
            Assert.Equal(RafterRoofFace.Face0, shared.Rafters[station * 2].Face);
            Assert.Equal(RafterRoofFace.Face1, shared.Rafters[station * 2 + 1].Face);
        }
    }

    private static RoofRafterLayout Layout(
        IRoofGeometry geometry,
        double maximumSpacingMm = MaximumSpacingMm,
        double rafterPlanWidthMm = RafterPlanWidthMm)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(maximumSpacingMm, rafterPlanWidthMm));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry SolveMonopitch(RoofDefinition definition)
    {
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static MonopitchRoofGeometry SolveMonopitch(
        RoofFootprintInput input,
        double slopeDegrees,
        RoofDirection2D direction) =>
        SolveMonopitch(MonopitchDefinition(Validate(input), slopeDegrees, direction));

    private static RoofDefinition MonopitchDefinition(
        RoofFootprint footprint,
        double slopeDegrees,
        RoofDirection2D direction) =>
        new(
            footprint,
            new RoofParameters(slopeDegrees, SlopeDirection: direction),
            RoofKind.Monopitch);

    private static SimpleGableRoofGeometry SolveGable(bool asymmetric)
    {
        var definition = new RoofDefinition(
            Validate(Rectangle(10000d, 6000d)),
            asymmetric
                ? new RoofParameters(
                    20d,
                    Direction(1d, 0d),
                    Face1SlopeDegrees: 35d,
                    EaveHeightDifferenceMm: 300d)
                : new RoofParameters(30d, Direction(1d, 0d)),
            asymmetric ? RoofKind.AsymmetricGable : RoofKind.SimpleGable);
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static void AssertLayoutsEqual(RoofRafterLayout expected, RoofRafterLayout actual)
    {
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.RequestedMaximumSpacingMm, actual.RequestedMaximumSpacingMm);
        Assert.Equal(expected.RafterPlanWidthMm, actual.RafterPlanWidthMm);
        Assert.Equal(expected.StationSpanMm, actual.StationSpanMm);
        Assert.Equal(expected.UsableCenterSpanMm, actual.UsableCenterSpanMm);
        Assert.Equal(expected.IntervalCount, actual.IntervalCount);
        Assert.Equal(expected.StationCount, actual.StationCount);
        Assert.Equal(expected.ActualSpacingMm, actual.ActualSpacingMm);
        Assert.Equal(expected.StationDirection, actual.StationDirection);
        Assert.Equal(expected.Planes, actual.Planes);
        Assert.Equal(expected.Rafters, actual.Rafters);
    }

    private static bool SameUndirectedSegment(RoofRafterGeometry first, RoofRafterGeometry second) =>
        first.PlanStart == second.PlanStart && first.PlanEnd == second.PlanEnd ||
        first.PlanStart == second.PlanEnd && first.PlanEnd == second.PlanStart;

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var result = RoofFootprintValidator.Validate(input);
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofFootprintInput Rectangle(double lengthMm, double widthMm) =>
        new(
            [
                new RoofPoint2D(0d, 0d),
                new RoofPoint2D(lengthMm, 0d),
                new RoofPoint2D(lengthMm, widthMm),
                new RoofPoint2D(0d, widthMm),
            ],
            true);

    private static RoofFootprintInput RotatedRectangle(
        double lengthMm,
        double widthMm,
        double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        var x = (X: Math.Cos(radians), Y: Math.Sin(radians));
        var y = (X: -Math.Sin(radians), Y: Math.Cos(radians));
        RoofPoint2D Point(double along, double across) =>
            new(
                350d + along * x.X + across * y.X,
                -725d + along * x.Y + across * y.Y);
        return new RoofFootprintInput(
            [
                Point(0d, 0d),
                Point(lengthMm, 0d),
                Point(lengthMm, widthMm),
                Point(0d, widthMm),
            ],
            true);
    }
}
