using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class RoofRafterTransientPreviewTests
{
    [Fact]
    public void HipTopologyAdapterMapsNeutralFaceSegmentsWithoutRecomputingGeometry()
    {
        var topologyResult = RoofTopologySolver.Solve(
            new RoofFootprintInput(
                [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)],
                true),
            30d);
        Assert.True(topologyResult.IsValid);
        var layoutResult = RoofFaceRafterLayoutService.Create(
            topologyResult.Topology!,
            500d);
        Assert.True(layoutResult.IsValid);

        var mapped = RoofTransientPreviewSession.MapRafterPlanSegments(
            layoutResult.Layout!);

        Assert.Equal(64, mapped.Count);
        Assert.Equal(
            layoutResult.Layout!.Segments.Select(segment => segment.PlanStart),
            mapped.Select(segment => segment.Start));
        Assert.Equal(
            layoutResult.Layout.Segments.Select(segment => segment.PlanEnd),
            mapped.Select(segment => segment.End));
        Assert.Equal(
            layoutResult.Layout.Segments.Select(segment => segment.SourceFaceIndex),
            mapped.Select(segment => segment.FaceIndex));
    }

    [Fact]
    public void MonopitchAdapterMapsNeutralPlanSegmentsInDeterministicOrder()
    {
        var geometry = MonopitchGeometry(10000, 6000, 30, 30);
        var layout = Layout(geometry, 850, 100);

        var segments = RoofTransientPreviewSession.MapRafterPlanSegments(layout);

        Assert.Equal(layout.Rafters.Count, segments.Count);
        Assert.Single(layout.Planes);
        for (var index = 0; index < segments.Count; index++)
        {
            var rafter = layout.Rafters[index];
            var segment = segments[index];
            Assert.Equal(rafter.PlanStart.X, segment.Start.X, 9);
            Assert.Equal(rafter.PlanStart.Y, segment.Start.Y, 9);
            Assert.Equal(rafter.PlanEnd.X, segment.End.X, 9);
            Assert.Equal(rafter.PlanEnd.Y, segment.End.Y, 9);
            Assert.Equal((int)RafterRoofFace.Face0, segment.FaceIndex);
            Assert.Equal(index, rafter.StationIndex);
        }
    }

    [Fact]
    public void MirroredMonopitchAdapterPreservesCountAndMapsReversedLowToHighDirection()
    {
        var first = Layout(MonopitchGeometry(10000, 6000, 30, 30), 900, 80);
        var mirrored = Layout(MonopitchGeometry(10000, 6000, 30, 210), 900, 80);

        var firstSegments = RoofTransientPreviewSession.MapRafterPlanSegments(first);
        var mirroredSegments = RoofTransientPreviewSession.MapRafterPlanSegments(mirrored);

        Assert.Equal(firstSegments.Count, mirroredSegments.Count);
        Assert.Equal(first.StationCount, mirrored.StationCount);
        Assert.All(first.Rafters.Zip(mirrored.Rafters), pair =>
        {
            Assert.Equal(pair.First.PlanLengthMm, pair.Second.PlanLengthMm, 8);
            Assert.Equal(pair.First.TrueLengthMm, pair.Second.TrueLengthMm, 8);
            Assert.Equal(-pair.First.RunDirection.X, pair.Second.RunDirection.X, 8);
            Assert.Equal(-pair.First.RunDirection.Y, pair.Second.RunDirection.Y, 8);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GableAdapterStillMapsTwoRafterFamilies(bool asymmetric)
    {
        var geometry = GableGeometry(asymmetric);
        var layout = Layout(geometry, 1000, 100);

        var segments = RoofTransientPreviewSession.MapRafterPlanSegments(layout);

        Assert.Equal(layout.StationCount * 2, segments.Count);
        Assert.Equal(2, layout.Planes.Count);
        for (var station = 0; station < layout.StationCount; station++)
        {
            Assert.Equal((int)RafterRoofFace.Face0, segments[station * 2].FaceIndex);
            Assert.Equal((int)RafterRoofFace.Face1, segments[station * 2 + 1].FaceIndex);
        }
    }

    [Fact]
    public void PreviewControllerDisposesOldSetBeforeReplacementAndCleansOnInvalidOrClose()
    {
        var firstLayout = Layout(MonopitchGeometry(10000, 6000, 30, 30), 1000, 80);
        var secondLayout = Layout(MonopitchGeometry(10000, 6000, 30, 30), 700, 80);
        var created = new List<TrackingDisposable>();
        var shown = new List<RoofRafterLayout>();
        var controller = new RoofRafterTransientPreviewController(layout =>
        {
            shown.Add(layout);
            var session = new TrackingDisposable();
            created.Add(session);
            return session;
        });

        controller.Refresh(firstLayout);
        controller.Refresh(secondLayout);

        Assert.Equal(new[] { firstLayout, secondLayout }, shown);
        Assert.True(created[0].IsDisposed);
        Assert.False(created[1].IsDisposed);

        controller.Refresh(null);

        Assert.True(created[1].IsDisposed);
        Assert.Equal(2, created.Count);

        controller.Refresh(firstLayout);
        controller.Dispose();
        controller.Dispose();

        Assert.True(created[2].IsDisposed);
        Assert.Equal(1, created[2].DisposeCount);
    }

    [Fact]
    public void HipPreviewControllerDisposesReplacementInvalidAndCloseWithoutPersistence()
    {
        var topologyResult = RoofTopologySolver.Solve(
            new RoofFootprintInput(
                [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)],
                true),
            30d);
        var first = RoofFaceRafterLayoutService.Create(
            topologyResult.Topology!,
            900d).Layout!;
        var second = RoofFaceRafterLayoutService.Create(
            topologyResult.Topology!,
            600d).Layout!;
        var created = new List<TrackingDisposable>();
        var controller = new RoofFaceRafterTransientPreviewController(layout =>
        {
            Assert.True(layout == first || layout == second);
            var session = new TrackingDisposable();
            created.Add(session);
            return session;
        });

        controller.Refresh(first);
        controller.Refresh(second);
        Assert.True(created[0].IsDisposed);

        controller.Refresh(null);
        Assert.True(created[1].IsDisposed);

        controller.Refresh(first);
        controller.Dispose();
        Assert.True(created[2].IsDisposed);
        Assert.Equal(1, created[2].DisposeCount);
    }

    private static RoofRafterLayout Layout(
        IRoofGeometry geometry,
        double spacing,
        double width)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(spacing, width));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry MonopitchGeometry(
        double length,
        double width,
        double slope,
        double directionDegrees)
    {
        var angle = 30d * Math.PI / 180d;
        var along = Direction(Math.Cos(angle), Math.Sin(angle));
        var across = Direction(-Math.Sin(angle), Math.Cos(angle));
        var points = new[]
        {
            new RoofPoint2D(0, 0),
            new RoofPoint2D(length * along.X, length * along.Y),
            new RoofPoint2D(
                length * along.X + width * across.X,
                length * along.Y + width * across.Y),
            new RoofPoint2D(width * across.X, width * across.Y),
        };
        var footprint = Validate(points);
        var directionRadians = directionDegrees * Math.PI / 180d;
        var slopeDirection = Direction(
            Math.Cos(directionRadians),
            Math.Sin(directionRadians));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, SlopeDirection: slopeDirection),
            RoofKind.Monopitch));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static SimpleGableRoofGeometry GableGeometry(bool asymmetric)
    {
        var footprint = Validate([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 8000),
            new RoofPoint2D(0, 8000),
        ]);
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            asymmetric
                ? new RoofParameters(20, Direction(1, 0), Face1SlopeDegrees: 35)
                : new RoofParameters(30, Direction(1, 0)),
            asymmetric ? RoofKind.AsymmetricGable : RoofKind.SimpleGable));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed => DisposeCount > 0;

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
