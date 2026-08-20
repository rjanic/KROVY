using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofFootprintContainmentRulesTests
{
    // Canonical CCW rectangle (matches SimpleGable footprints).
    private static readonly RoofPoint2D[] Rectangle =
    [
        new(0d, 0d),
        new(10000d, 0d),
        new(10000d, 6000d),
        new(0d, 6000d),
    ];

    // Concave U-shape: legs at x in [0,2] and [8,10], top bar at y in [8,10].
    private static readonly RoofPoint2D[] UShape =
    [
        new(0d, 0d),
        new(0d, 10d),
        new(10d, 10d),
        new(10d, 0d),
        new(8d, 0d),
        new(8d, 8d),
        new(2d, 8d),
        new(2d, 0d),
    ];

    [Fact]
    public void FullyInsideSegment_IsContained()
    {
        Assert.True(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(2000d, 2000d),
            new RoofPoint2D(8000d, 4000d),
            Rectangle));
    }

    [Fact]
    public void SegmentAlongBoundaryEdge_IsContained()
    {
        // Entire segment lies exactly on the bottom footprint edge.
        Assert.True(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(1000d, 0d),
            new RoofPoint2D(9000d, 0d),
            Rectangle));
    }

    [Fact]
    public void SegmentEndpointsOnOppositeBoundaries_IsContained()
    {
        // Endpoints on the left/right edges, interior strictly inside.
        Assert.True(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(0d, 3000d),
            new RoofPoint2D(10000d, 3000d),
            Rectangle));
    }

    [Fact]
    public void PartlyOutsideSegment_IsNotContained()
    {
        // One endpoint inside, one endpoint past the right edge.
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(2000d, 2000d),
            new RoofPoint2D(12000d, 3000d),
            Rectangle));
    }

    [Fact]
    public void FullyOutsideSegment_IsNotContained()
    {
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(12000d, 1000d),
            new RoofPoint2D(15000d, 5000d),
            Rectangle));
    }

    [Fact]
    public void SegmentCrossingConcaveNotch_IsNotContained()
    {
        // Both endpoints inside the two legs, but the straight segment exits through
        // the U-shape notch.
        Assert.True(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(1d, 1d),
            UShape));
        Assert.True(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(9d, 1d),
            UShape));
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(1d, 1d),
            new RoofPoint2D(9d, 1d),
            UShape));
    }

    [Fact]
    public void PointOnVertex_IsInsideOrOnBoundary()
    {
        Assert.True(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(0d, 0d),
            Rectangle));
    }

    [Fact]
    public void PointStrictlyOutside_IsNotInside()
    {
        Assert.False(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(11000d, 3000d),
            Rectangle));
    }

    [Fact]
    public void DegeneratePolygon_IsNotContained()
    {
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(1d, 1d),
            new[] { new RoofPoint2D(0d, 0d), new RoofPoint2D(1d, 0d) }));
    }

    [Fact]
    public void BoundaryWithinTolerance_IsContained()
    {
        // A hair inside the right edge still counts as on-boundary.
        Assert.True(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(10000d - RoofFootprintContainmentRules.ContainmentToleranceMm / 2d, 3000d),
            Rectangle));
    }
}
