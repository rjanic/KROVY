using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcKrovy.AutoCAD.UI;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class MonopitchRoofSectionControlTests
{
    [Theory]
    [InlineData(5d)]
    [InlineData(25d)]
    [InlineData(35d)]
    [InlineData(45d)]
    [InlineData(75d)]
    [InlineData(85d)]
    public void AngleReferenceMaintainsRoofStrokeClearance(double slope)
    {
        foreach (var mirrored in new[] { false, true })
        {
            var layout = Layout(slope, mirrored, 560d, 330d);
            var renderedSlope = Math.Atan2(
                layout.Low.Y - layout.High.Y,
                Math.Abs(layout.High.X - layout.Low.X)) * 180d / Math.PI;
            var low = new Point(layout.Low.X, layout.Low.Y);
            var high = new Point(layout.High.X, layout.High.Y);
            var annotation = MonopitchRoofSectionControl.CreateAngleAnnotation(low, high);
            var referenceCenterlineClearance = SegmentDistance(
                annotation.ReferenceStart,
                annotation.ReferenceEnd,
                low,
                high);
            var renderedStrokeHalfWidths =
                RoofSectionDiagramStyle.RoofFaceStrokeThickness / 2d +
                RoofSectionDiagramStyle.AngleStrokeThickness / 2d;

            Assert.Equal(slope, renderedSlope, 8);
            Assert.True(
                referenceCenterlineClearance - renderedStrokeHalfWidths >=
                RoofSectionDiagramStyle.AngleSafeRoofClearance - 0.001d);
            Assert.True(annotation.Anchor.Y < annotation.RoofCenterlinePoint.Y);
            Assert.True(annotation.LabelOrigin.Y < annotation.Anchor.Y - 15d);
            Assert.Equal(
                mirrored,
                annotation.Anchor.X > annotation.RoofCenterlinePoint.X);
        }
    }

    [Theory]
    [InlineData(5d)]
    [InlineData(25d)]
    [InlineData(35d)]
    [InlineData(45d)]
    [InlineData(75d)]
    [InlineData(85d)]
    public void AngleArcVertexIsHorizontalReferenceRoofIntersection(double slope)
    {
        foreach (var mirrored in new[] { false, true })
        {
            var layout = Layout(slope, mirrored, 560d, 330d);
            var low = new Point(layout.Low.X, layout.Low.Y);
            var high = new Point(layout.High.X, layout.High.Y);
            var annotation = MonopitchRoofSectionControl.CreateAngleAnnotation(low, high);
            var vertex = GableRoofSectionControl.CreateAngleArcVertex(
                annotation,
                low,
                high);

            Assert.Equal(annotation.ReferenceStart.Y, vertex.Y, 10);
            Assert.Equal(annotation.ReferenceEnd.Y, vertex.Y, 10);
            Assert.Equal(0d, Cross(low, high, vertex), 8);
            Assert.Equal(mirrored, low.X > vertex.X);
        }
    }

    [Fact]
    public void ConstructionProjectionMovesToPhysicalHighSideAfterMirror()
    {
        var normal = Layout(25d, false, 560d, 330d);
        var mirrored = Layout(25d, true, 560d, 330d);

        Assert.Equal(normal.Low.Y, normal.HighProjection.Y, 10);
        Assert.Equal(normal.High.X, normal.HighProjection.X, 10);
        Assert.True(normal.High.X > normal.Low.X);

        Assert.Equal(mirrored.Low.Y, mirrored.HighProjection.Y, 10);
        Assert.Equal(mirrored.High.X, mirrored.HighProjection.X, 10);
        Assert.True(mirrored.High.X < mirrored.Low.X);

        Assert.Equal(normal.Low.Y, mirrored.Low.Y, 10);
        Assert.Equal(normal.High.Y, mirrored.High.Y, 10);
        Assert.Equal(normal.High.X, mirrored.Low.X, 10);
        Assert.Equal(normal.Low.X, mirrored.High.X, 10);
    }

    [Theory]
    [InlineData(360d, 260d)]
    [InlineData(560d, 330d)]
    [InlineData(800d, 500d)]
    public void UniformLayoutAdaptsToSupportedViewportAspectRatios(
        double width,
        double height)
    {
        var layout = Layout(45d, false, width, height);

        Assert.True(layout.Left >= 0d);
        Assert.True(layout.Right <= width);
        Assert.True(layout.Top >= 0d);
        Assert.True(layout.Bottom <= height);
        Assert.Equal(
            layout.Scale,
            (layout.Right - layout.Left) / 6000d,
            12);
        Assert.Equal(
            45d,
            Math.Atan2(
                layout.Low.Y - layout.High.Y,
                layout.High.X - layout.Low.X) * 180d / Math.PI,
            8);
    }

    [Fact]
    public void SharedStyleProvidesGableConstructionAndRoofFaceContracts()
    {
        var datum = RoofSectionDiagramStyle.CreateDatumPen(Brushes.Gray);
        var reference = RoofSectionDiagramStyle.CreateReferencePen(Brushes.DimGray);
        var dimension = RoofSectionDiagramStyle.CreateDimensionPen(Brushes.DimGray);
        var roof = RoofSectionDiagramStyle.CreateRoofFacePen(
            RoofSectionDiagramStyle.GreenRoofFaceBrush);

        Assert.Equal(DashStyles.Dash, datum.DashStyle);
        Assert.Equal(DashStyles.Dash, reference.DashStyle);
        Assert.Equal(RoofSectionDiagramStyle.DatumStrokeThickness, datum.Thickness);
        Assert.Equal(RoofSectionDiagramStyle.ReferenceStrokeThickness, reference.Thickness);
        Assert.Equal(RoofSectionDiagramStyle.DimensionStrokeThickness, dimension.Thickness);
        Assert.Equal(RoofSectionDiagramStyle.RoofFaceStrokeThickness, roof.Thickness);
        Assert.Same(RoofSectionDiagramStyle.GreenRoofFaceBrush, roof.Brush);
        Assert.Equal(80d, RoofSectionDiagramStyle.AngleArcRadius);
    }

    private static MonopitchRoofSectionLayout Layout(
        double slope,
        bool mirrored,
        double width,
        double height)
    {
        const double span = 6000d;
        var state = new MonopitchRoofSectionState(
            span,
            span * Math.Tan(slope * Math.PI / 180d),
            slope,
            mirrored,
            "LOW",
            "HIGH",
            "Span",
            CultureInfo.InvariantCulture);
        return Assert.IsType<MonopitchRoofSectionLayout>(
            MonopitchRoofSectionLayoutCalculator.Create(state, width, height));
    }

    private static double SegmentDistance(
        Point firstStart,
        Point firstEnd,
        Point secondStart,
        Point secondEnd)
    {
        if (SegmentsIntersect(firstStart, firstEnd, secondStart, secondEnd))
        {
            return 0d;
        }

        return new[]
        {
            PointToSegmentDistance(firstStart, secondStart, secondEnd),
            PointToSegmentDistance(firstEnd, secondStart, secondEnd),
            PointToSegmentDistance(secondStart, firstStart, firstEnd),
            PointToSegmentDistance(secondEnd, firstStart, firstEnd),
        }.Min();
    }

    private static double PointToSegmentDistance(Point point, Point start, Point end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var squaredLength = deltaX * deltaX + deltaY * deltaY;
        var ratio = squaredLength <= double.Epsilon
            ? 0d
            : Math.Clamp(
                ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) /
                squaredLength,
                0d,
                1d);
        var nearestX = start.X + ratio * deltaX;
        var nearestY = start.Y + ratio * deltaY;
        var distanceX = point.X - nearestX;
        var distanceY = point.Y - nearestY;
        return Math.Sqrt(distanceX * distanceX + distanceY * distanceY);
    }

    private static bool SegmentsIntersect(
        Point firstStart,
        Point firstEnd,
        Point secondStart,
        Point secondEnd)
    {
        var firstA = Cross(firstStart, firstEnd, secondStart);
        var firstB = Cross(firstStart, firstEnd, secondEnd);
        var secondA = Cross(secondStart, secondEnd, firstStart);
        var secondB = Cross(secondStart, secondEnd, firstEnd);
        return firstA * firstB <= 0d && secondA * secondB <= 0d;
    }

    private static double Cross(Point start, Point end, Point point) =>
        (end.X - start.X) * (point.Y - start.Y) -
        (end.Y - start.Y) * (point.X - start.X);

}
