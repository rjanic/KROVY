using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Behavioral proof that the source footprint polygon (not its bounding box, and not
/// any group/annotation extents) is the sharp inside/outside authority. Bounding-box
/// enclosure does not imply containment; a point inside the bbox but in a concave
/// notch is outside. A segment just beyond the boundary behaves exactly like one far
/// outside — there is no "near roof" grace zone.
/// </summary>
public sealed class RoofFootprintAuthorityBehaviorTests
{
    // Canonical CCW rectangle (SimpleGable footprint).
    private static readonly RoofPoint2D[] Rectangle =
    [
        new(0d, 0d),
        new(10000d, 0d),
        new(10000d, 6000d),
        new(0d, 6000d),
    ];

    // U-shape opening downward: notch spans x in (2,8), y in (0,8).
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
    public void PointInsideBoundingBoxButInConcaveNotch_IsOutside()
    {
        // (5,4) is inside the axis-aligned bbox [0,10]x[0,10] but lies in the U-shape
        // notch, which is NOT part of the footprint. Bounding-box enclosure must never
        // be used as the roof boundary.
        Assert.True(RoofBoundingBox2DContains(UShape, new RoofPoint2D(5d, 4d)));
        Assert.False(RoofFootprintContainmentRules.IsPointInsideOrOnBoundary(
            new RoofPoint2D(5d, 4d),
            UShape));
    }

    [Fact]
    public void SegmentJustOutsideBoundary_IsNotContained_SameAsFarOutside()
    {
        var tolerance = RoofFootprintContainmentRules.ContainmentToleranceMm;
        var justOutside = tolerance * 2d;

        // A hair beyond the right edge — NOT contained (no near-roof grace zone).
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(10000d + justOutside, 1000d),
            new RoofPoint2D(10000d + justOutside, 5000d),
            Rectangle));

        // Far outside — identical outcome.
        Assert.False(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(15000d, 1000d),
            new RoofPoint2D(15000d, 5000d),
            Rectangle));
    }

    [Fact]
    public void SegmentJustInsideBoundary_IsContained_SharpTolerance()
    {
        var tolerance = RoofFootprintContainmentRules.ContainmentToleranceMm;

        // A hair inside the right edge — contained (on-boundary within tolerance).
        Assert.True(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            new RoofPoint2D(10000d - tolerance / 2d, 1000d),
            new RoofPoint2D(10000d - tolerance / 2d, 5000d),
            Rectangle));
    }

    [Fact]
    public void ContainmentDecision_DependsOnlyOnSegmentAndFootprintVertices()
    {
        // The containment API has no group, annotation, or extents input. The same
        // segment + vertices always yield the same result — there is nothing that could
        // make an overlapping annotation keep a child "alive". This is the behavioral
        // face of "annotation extents do not affect containment".
        var segment = (
            Start: new RoofPoint2D(12000d, 1000d),
            End: new RoofPoint2D(15000d, 5000d));
        var first = RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            segment.Start,
            segment.End,
            Rectangle);
        var second = RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(
            segment.Start,
            segment.End,
            Rectangle);
        Assert.Equal(first, second);
        Assert.False(first);
    }

    private static bool RoofBoundingBox2DContains(IReadOnlyList<RoofPoint2D> vertices, RoofPoint2D point)
    {
        var minX = vertices.Min(v => v.X);
        var minY = vertices.Min(v => v.Y);
        var maxX = vertices.Max(v => v.X);
        var maxY = vertices.Max(v => v.Y);
        return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
    }
}
