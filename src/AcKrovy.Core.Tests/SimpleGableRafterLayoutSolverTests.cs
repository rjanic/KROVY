using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SimpleGableRafterLayoutSolverTests
{
    [Fact]
    public void TenMetreRidge_OneMetreMaximum_CreatesExpectedLayout()
    {
        var layout = Layout(Geometry(10000, 8000, 30), 1000);

        Assert.Equal(10, layout.IntervalCount);
        Assert.Equal(11, layout.StationCount);
        Assert.Equal(22, layout.Rafters.Count);
        Assert.Equal(990d, layout.ActualSpacingMm, 9);
        Assert.Equal(100d, layout.RafterPlanWidthMm);
        Assert.Equal(9900d, layout.UsableCenterSpanMm);
        Assert.Equal(11, layout.Rafters.Count(item => item.Face == RafterRoofFace.Face0));
        Assert.Equal(11, layout.Rafters.Count(item => item.Face == RafterRoofFace.Face1));
    }

    [Fact]
    public void TenMetreRidge_NineHundredMaximum_EqualizesBelowMaximum()
    {
        var layout = Layout(Geometry(10000, 8000, 30), 900);

        Assert.Equal(11, layout.IntervalCount);
        Assert.Equal(12, layout.StationCount);
        Assert.Equal(24, layout.Rafters.Count);
        Assert.Equal(900d, layout.ActualSpacingMm, 9);
        Assert.True(layout.ActualSpacingMm <= layout.RequestedMaximumSpacingMm);
    }

    [Fact]
    public void FirstAndLastCenterlinesAreHalfWidthInsideGablePlanes()
    {
        var geometry = Geometry(10000, 8000, 30);
        var layout = Layout(geometry, 900);

        AssertStation(layout, 0, 50d / 10000d, geometry);
        AssertStation(layout, layout.StationCount - 1, 9950d / 10000d, geometry);
    }

    [Fact]
    public void InteriorStationsAreFractionBasedEqualizedAndNeverDuplicated()
    {
        var layout = Layout(Geometry(10000, 8000, 30), 900);
        var fractions = layout.Rafters
            .Where(item => item.Face == RafterRoofFace.Face0)
            .Select(item => item.StationFraction)
            .ToArray();

        Assert.Equal(layout.StationCount, fractions.Distinct().Count());
        for (var index = 0; index < fractions.Length; index++)
        {
            var expectedDistance = layout.RafterPlanWidthMm / 2d +
                                   layout.UsableCenterSpanMm * index / layout.IntervalCount;
            Assert.Equal(expectedDistance / layout.RidgeLengthMm, fractions[index], 12);
        }
        Assert.Equal(
            layout.Rafters.Count,
            layout.Rafters.Select(SegmentKey).Distinct().Count());
    }

    [Fact]
    public void BothFacesRunFromEaveToRidgeAndPropagateSlope()
    {
        var geometry = Geometry(10000, 8000, 37);
        var layout = Layout(geometry, 1000);

        foreach (var rafter in layout.Rafters)
        {
            var face = geometry.Faces[(int)rafter.Face];
            Assert.True(IsOnSegment(rafter.PlanStart, face.Eave));
            Assert.True(IsOnSegment(rafter.PlanEnd, geometry.Ridge));
            Assert.Equal(37d, rafter.SlopeDegrees);
            Assert.True(Distance(rafter.PlanStart, rafter.PlanEnd) > 0d);
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonPositiveOrNonFiniteSpacingIsRejected(double spacing)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(
            Geometry(10000, 8000, 30),
            new RafterLayoutParameters(spacing, 100));

        Assert.False(result.IsValid);
        Assert.Null(result.Layout);
        Assert.Equal(SimpleGableRafterLayoutError.InvalidMaximumSpacing, result.Error);
    }

    [Fact]
    public void MaximumLargerThanRidgeStillCreatesBothEndStations()
    {
        var layout = Layout(Geometry(10000, 8000, 30), 20000);

        Assert.Equal(1, layout.IntervalCount);
        Assert.Equal(2, layout.StationCount);
        Assert.Equal(4, layout.Rafters.Count);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(10000d)]
    [InlineData(11000d)]
    public void InvalidOrNonFittingPlanWidthIsRejected(double width)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(
            Geometry(10000, 8000, 30),
            new RafterLayoutParameters(1000, width));

        Assert.False(result.IsValid);
        Assert.Equal(SimpleGableRafterLayoutError.InvalidRafterPlanWidth, result.Error);
    }

    [Fact]
    public void ThirtyDegreeRotatedRoofHasNoWorldAxisAssumption()
    {
        var geometry = RotatedGeometry(30, 10000, 8000, 30, alternateRidge: false);
        var layout = Layout(geometry, 1000);

        Assert.Equal(22, layout.Rafters.Count);
        Assert.NotEqual(layout.Rafters[0].PlanStart.X, layout.Rafters[0].PlanEnd.X);
        Assert.NotEqual(layout.Rafters[0].PlanStart.Y, layout.Rafters[0].PlanEnd.Y);
        Assert.All(layout.Rafters, item => Assert.True(IsPerpendicularInPlan(item, geometry)));
    }

    [Fact]
    public void SquareHonoursSelectedAlternateRidgeFamily()
    {
        var alongX = Geometry(6000, 6000, 30, ridgeAlongX: true);
        var alongY = Geometry(6000, 6000, 30, ridgeAlongX: false);
        var xLayout = Layout(alongX, 1000);
        var yLayout = Layout(alongY, 1000);

        Assert.True(IsPerpendicularInPlan(xLayout.Rafters[0], alongX));
        Assert.True(IsPerpendicularInPlan(yLayout.Rafters[0], alongY));
        Assert.NotEqual(xLayout.Signature, yLayout.Signature);
    }

    [Fact]
    public void SquareRotatedNinetyDegreesRetainsRequestedFamily()
    {
        var geometry = RotatedGeometry(90, 6000, 6000, 30, alternateRidge: true);
        var layout = Layout(geometry, 1000);

        Assert.All(layout.Rafters, item => Assert.True(IsPerpendicularInPlan(item, geometry)));
    }

    [Fact]
    public void EquivalentClockwiseAndCounterClockwiseRoofsProduceSameLayout()
    {
        var first = SolveGeometry(
            [P(0, 0), P(10000, 0), P(10000, 8000), P(0, 8000)], 30, 1, 0);
        var second = SolveGeometry(
            [P(0, 8000), P(10000, 8000), P(10000, 0), P(0, 0)], 30, -1, 0);

        AssertLayoutsEqual(Layout(first, 900), Layout(second, 900));
    }

    [Fact]
    public void RepeatedSolveIsDeterministicAndDoesNotMutateGeometry()
    {
        var geometry = RotatedGeometry(17, 10000, 8000, 33, alternateRidge: false);
        var before = geometry.Signature;
        var expected = Layout(geometry, 777);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            AssertLayoutsEqual(expected, Layout(geometry, 777));
        }
        Assert.Equal(before, geometry.Signature);
    }

    private static void AssertStation(
        SimpleGableRafterLayout layout,
        int stationIndex,
        double fraction,
        SimpleGableRoofGeometry geometry)
    {
        var station = layout.Rafters.Where(item => item.StationIndex == stationIndex).ToArray();
        Assert.Equal(2, station.Length);
        Assert.All(station, item => Assert.Equal(fraction, item.StationFraction));
        Assert.Equal(PointAt(geometry.Ridge, fraction), station[0].PlanEnd);
        Assert.Equal(PointAt(geometry.Ridge, fraction), station[1].PlanEnd);
        Assert.Equal(PointAt(geometry.Faces[0].Eave, fraction), station[0].PlanStart);
        Assert.Equal(PointAt(geometry.Faces[1].Eave, fraction), station[1].PlanStart);
    }

    private static SimpleGableRafterLayout Layout(
        SimpleGableRoofGeometry geometry,
        double spacing,
        double rafterPlanWidth = 100d)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(spacing, rafterPlanWidth));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static void AssertLayoutsEqual(
        SimpleGableRafterLayout expected,
        SimpleGableRafterLayout actual)
    {
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.RequestedMaximumSpacingMm, actual.RequestedMaximumSpacingMm);
        Assert.Equal(expected.RafterPlanWidthMm, actual.RafterPlanWidthMm);
        Assert.Equal(expected.RidgeLengthMm, actual.RidgeLengthMm);
        Assert.Equal(expected.UsableCenterSpanMm, actual.UsableCenterSpanMm);
        Assert.Equal(expected.IntervalCount, actual.IntervalCount);
        Assert.Equal(expected.StationCount, actual.StationCount);
        Assert.Equal(expected.ActualSpacingMm, actual.ActualSpacingMm);
        Assert.Equal(expected.Rafters, actual.Rafters);
    }

    private static SimpleGableRoofGeometry Geometry(
        double length,
        double width,
        double slope,
        bool ridgeAlongX = true) =>
        SolveGeometry(
            [P(0, 0), P(length, 0), P(length, width), P(0, width)],
            slope,
            ridgeAlongX ? 1 : 0,
            ridgeAlongX ? 0 : 1);

    private static SimpleGableRoofGeometry RotatedGeometry(
        double angleDegrees,
        double length,
        double width,
        double slope,
        bool alternateRidge)
    {
        var radians = angleDegrees * Math.PI / 180d;
        var axis = (X: Math.Cos(radians), Y: Math.Sin(radians));
        var transverse = (X: -axis.Y, Y: axis.X);
        RoofPoint2D Corner(double along, double across) =>
            P(axis.X * along + transverse.X * across, axis.Y * along + transverse.Y * across);
        return SolveGeometry(
            [
                Corner(-length / 2d, -width / 2d),
                Corner(length / 2d, -width / 2d),
                Corner(length / 2d, width / 2d),
                Corner(-length / 2d, width / 2d),
            ],
            slope,
            alternateRidge ? transverse.X : axis.X,
            alternateRidge ? transverse.Y : axis.Y);
    }

    private static SimpleGableRoofGeometry SolveGeometry(
        IReadOnlyList<RoofPoint2D> vertices,
        double slope,
        double directionX,
        double directionY)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.True(RoofDirection2D.TryCreate(directionX, directionY, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static bool IsPerpendicularInPlan(
        SimpleGableRafter rafter,
        SimpleGableRoofGeometry geometry)
    {
        var rafterX = rafter.PlanEnd.X - rafter.PlanStart.X;
        var rafterY = rafter.PlanEnd.Y - rafter.PlanStart.Y;
        var ridgeX = geometry.Ridge.End.X - geometry.Ridge.Start.X;
        var ridgeY = geometry.Ridge.End.Y - geometry.Ridge.Start.Y;
        var denominator = Math.Sqrt(
            (rafterX * rafterX + rafterY * rafterY) *
            (ridgeX * ridgeX + ridgeY * ridgeY));
        return Math.Abs((rafterX * ridgeX + rafterY * ridgeY) / denominator) < 1e-10;
    }

    private static bool IsOnSegment(RoofPoint2D point, RoofSegment3D segment)
    {
        var start = new RoofPoint2D(segment.Start.X, segment.Start.Y);
        var end = new RoofPoint2D(segment.End.X, segment.End.Y);
        var cross = (point.X - start.X) * (end.Y - start.Y) -
                    (point.Y - start.Y) * (end.X - start.X);
        return Math.Abs(cross) < 1e-6 &&
               point.X >= Math.Min(start.X, end.X) - 1e-7 &&
               point.X <= Math.Max(start.X, end.X) + 1e-7 &&
               point.Y >= Math.Min(start.Y, end.Y) - 1e-7 &&
               point.Y <= Math.Max(start.Y, end.Y) + 1e-7;
    }

    private static RoofPoint2D PointAt(RoofSegment3D segment, double fraction) =>
        new(
            segment.Start.X + (segment.End.X - segment.Start.X) * fraction,
            segment.Start.Y + (segment.End.Y - segment.Start.Y) * fraction);

    private static string SegmentKey(SimpleGableRafter rafter) =>
        $"{rafter.PlanStart.X:R},{rafter.PlanStart.Y:R}>{rafter.PlanEnd.X:R},{rafter.PlanEnd.Y:R}";

    private static double Distance(RoofPoint2D first, RoofPoint2D second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static RoofPoint2D P(double x, double y) => new(x, y);
}
