using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberLineRectangleDiscoveryTests
{
    [Fact]
    public void AxisAlignedRectangle_IsDiscoveredFromSelectedEdge()
    {
        var result = Discover("A", Rectangle());

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.Equal(4, result.OrderedEdgeKeys.Count);
        Assert.Equal("A", result.OrderedEdgeKeys[0]);
        Assert.Equal(140d, result.Geometry!.Segments[0].LengthMm, 6);
        Assert.Equal(200d, result.Geometry.Segments[1].LengthMm, 6);
    }

    [Fact]
    public void RotatedRectangle_IsDiscovered()
    {
        var result = Discover("A", Rotate(Rectangle(), 31d));

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.Equal(28_000d, result.Geometry!.AreaMm2, 5);
    }

    [Fact]
    public void Square_IsDiscovered()
    {
        var result = Discover("A", Rectangle(140d, 140d));

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.All(result.Geometry!.Segments, segment => Assert.Equal(140d, segment.LengthMm, 6));
    }

    [Fact]
    public void ReversedEntityDirections_AreOrderedIntoCycle()
    {
        var edges = Rectangle()
            .Select((edge, index) => index % 2 == 0 ? edge : edge with { Start = edge.End, End = edge.Start })
            .ToArray();

        var result = Discover("A", edges);

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.True(result.Geometry!.AreaMm2 > 0d);
    }

    [Theory]
    [InlineData("A", 140d, 200d)]
    [InlineData("B", 200d, 140d)]
    [InlineData("C", 140d, 200d)]
    [InlineData("D", 200d, 140d)]
    public void AnySelectedSide_BecomesWidthEdgeZero(
        string selectedKey,
        double expectedWidth,
        double expectedHeight)
    {
        var result = Discover(selectedKey, Rectangle());

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.Equal(selectedKey, result.OrderedEdgeKeys[0]);
        Assert.Equal(TimberLineRectangleDiscoveryResult.SelectedWidthEdgeIndex, 0);
        Assert.Equal(expectedWidth, result.Geometry!.Segments[0].LengthMm, 6);
        Assert.Equal(expectedHeight, result.Geometry.Segments[1].LengthMm, 6);
    }

    [Fact]
    public void EndpointDeviationInsideTolerance_IsAcceptedWithoutAveragingCorners()
    {
        var delta = TimberLineRectangleDiscoveryService.EndpointConnectivityToleranceMm * 0.5d;
        var edges = Rectangle();
        edges[1] = edges[1] with { Start = P(140d + delta, 0d) };

        var result = Discover("A", edges);

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, result.Status);
        Assert.Equal(P(140d, 0d), result.Geometry!.Vertices[1]);
        Assert.DoesNotContain(result.Geometry.Vertices, point => point == P(140d + delta / 2d, 0d));
    }

    [Fact]
    public void EndpointGapOutsideTolerance_IsRejected()
    {
        var gap = TimberLineRectangleDiscoveryService.EndpointConnectivityToleranceMm * 2d;
        var edges = Rectangle();
        edges[1] = edges[1] with { Start = P(140d + gap, 0d) };

        Assert.Equal(TimberLineRectangleDiscoveryStatus.NotFound, Discover("A", edges).Status);
    }

    [Fact]
    public void OpenChain_IsRejected()
    {
        Assert.Equal(
            TimberLineRectangleDiscoveryStatus.NotFound,
            Discover("A", Rectangle().Take(3).ToArray()).Status);
    }

    [Fact]
    public void FiveEdgeClosedCycle_IsRejected()
    {
        var edges = new[]
        {
            E("A", P(0, 0), P(100, 0)),
            E("B", P(100, 0), P(140, 80)),
            E("C", P(140, 80), P(70, 140)),
            E("D", P(70, 140), P(0, 80)),
            E("E", P(0, 80), P(0, 0)),
        };

        Assert.Equal(TimberLineRectangleDiscoveryStatus.NotFound, Discover("A", edges).Status);
    }

    [Fact]
    public void Trapezoid_IsRejected()
    {
        var edges = FromVertices(P(0, 0), P(140, 0), P(120, 200), P(20, 200));

        Assert.Equal(TimberLineRectangleDiscoveryStatus.InvalidRectangle, Discover("A", edges).Status);
    }

    [Fact]
    public void Parallelogram_IsRejected()
    {
        var edges = FromVertices(P(0, 0), P(140, 0), P(180, 200), P(40, 200));

        Assert.Equal(TimberLineRectangleDiscoveryStatus.InvalidRectangle, Discover("A", edges).Status);
    }

    [Fact]
    public void BranchAtRectangleCorner_IsRejected()
    {
        var edges = Rectangle().Append(E("X", P(0, 0), P(-50, -50))).ToArray();

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Branching, Discover("A", edges).Status);
    }

    [Fact]
    public void TJunctionAtEdgeInterior_IsRejected()
    {
        var edges = Rectangle().Append(E("X", P(70, 0), P(70, -50))).ToArray();

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Branching, Discover("A", edges).Status);
    }

    [Fact]
    public void TwoRectanglesContainingSelectedEdge_AreAmbiguous()
    {
        var edges = Rectangle().Concat(
        [
            E("E", P(140, 0), P(140, -100)),
            E("F", P(140, -100), P(0, -100)),
            E("G", P(0, -100), P(0, 0)),
        ]).ToArray();

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Ambiguous, Discover("A", edges).Status);
    }

    [Fact]
    public void DuplicateGeometricEdge_IsRejected()
    {
        var edges = Rectangle().Append(E("X", P(140, 0), P(0, 0))).ToArray();

        Assert.Equal(TimberLineRectangleDiscoveryStatus.DuplicateEdge, Discover("A", edges).Status);
    }

    [Fact]
    public void DisconnectedDrawingLines_DoNotAffectLocalDiscovery()
    {
        var edges = Rectangle().Concat(
        [
            E("X", P(1000, 1000), P(1100, 1000)),
            E("Y", P(1000, 1000), P(1100, 1000)),
        ]).ToArray();

        Assert.Equal(TimberLineRectangleDiscoveryStatus.Success, Discover("A", edges).Status);
    }

    [Fact]
    public void ConversionPlan_ContainsFourClosedStraightVerticesAndFourSources()
    {
        var discovery = Discover("A", Rectangle());

        var plan = TimberLineRectangleConversionPlan.FromDiscovery(discovery);

        Assert.Equal(4, plan.Vertices.Count);
        Assert.Equal(4, plan.SourceEdgeKeys.Count);
        Assert.True(plan.IsClosed);
        Assert.Equal([0d, 0d, 0d, 0d], plan.Bulges);
        Assert.Equal(0, plan.WidthEdgeIndex);
        Assert.Equal(discovery.Geometry!.Vertices, plan.Vertices);
    }

    [Fact]
    public void ConversionPlan_RejectsFailedDiscovery()
    {
        var discovery = Discover("A", Rectangle().Take(3).ToArray());

        Assert.Throws<ArgumentException>(() => TimberLineRectangleConversionPlan.FromDiscovery(discovery));
    }

    private static TimberLineRectangleDiscoveryResult Discover(
        string selectedKey,
        IReadOnlyList<TimberLineRectangleEdge> edges) =>
        TimberLineRectangleDiscoveryService.Discover(selectedKey, edges);

    private static TimberLineRectangleEdge[] Rectangle(double width = 140d, double height = 200d) =>
        FromVertices(P(0, 0), P(width, 0), P(width, height), P(0, height));

    private static TimberLineRectangleEdge[] FromVertices(params TimberRectangularFootprintPoint[] vertices) =>
    [
        E("A", vertices[0], vertices[1]),
        E("B", vertices[1], vertices[2]),
        E("C", vertices[2], vertices[3]),
        E("D", vertices[3], vertices[0]),
    ];

    private static TimberLineRectangleEdge[] Rotate(
        IReadOnlyList<TimberLineRectangleEdge> edges,
        double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        TimberRectangularFootprintPoint RotatePoint(TimberRectangularFootprintPoint point) =>
            P(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine);
        return edges.Select(edge => edge with
        {
            Start = RotatePoint(edge.Start),
            End = RotatePoint(edge.End),
        }).ToArray();
    }

    private static TimberLineRectangleEdge E(
        string key,
        TimberRectangularFootprintPoint start,
        TimberRectangularFootprintPoint end) => new(key, start, end);

    private static TimberRectangularFootprintPoint P(double x, double y) => new(x, y);
}
