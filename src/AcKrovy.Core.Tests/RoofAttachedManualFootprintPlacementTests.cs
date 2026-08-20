using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualFootprintPlacementTests
{
    private static readonly RoofPoint3D PlaneNormal = new(0d, 0d, 1d);

    private static readonly RoofPoint2D[] SourceFootprint =
    [
        new(0d, 0d),
        new(10000d, 0d),
        new(10000d, 6000d),
        new(0d, 6000d),
    ];

    [Fact]
    public void MapSegment_WiderFootprint_PreservesLengthAndAngle_ForDiagonalCopy()
    {
        var target = WiderFootprint(12000d);
        var start = new RoofPoint3D(2000d, 1000d, 0d);
        var end = new RoofPoint3D(8000d, 4000d, 0d);
        var oldLength = Length(start, end);
        var oldAngle = LocalAngle(start, end);

        Assert.True(TryMap(start, end, target, out var mappedStart, out var mappedEnd, out var metrics));
        Assert.True(metrics.GeometryPreserved);
        Assert.InRange(Length(mappedStart, mappedEnd), oldLength - 0.01, oldLength + 0.01);
        Assert.InRange(LocalAngle(mappedStart, mappedEnd), oldAngle - 1e-6, oldAngle + 1e-6);
    }

    [Fact]
    public void MapSegment_NarrowerFootprint_PreservesLengthAndAngle()
    {
        var target = WiderFootprint(7000d);
        var start = new RoofPoint3D(1500d, 2000d, 0d);
        var end = new RoofPoint3D(6500d, 5000d, 0d);
        var oldLength = Length(start, end);

        Assert.True(TryMap(start, end, target, out var mappedStart, out var mappedEnd, out var metrics));
        Assert.InRange(Length(mappedStart, mappedEnd), oldLength - 0.01, oldLength + 0.01);
        Assert.True(metrics.GeometryPreserved);
    }

    [Fact]
    public void MapSegment_StrongAspectRatioChange_DoesNotShearSegment()
    {
        var target = new[]
        {
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(20000d, 0d),
            new RoofPoint2D(20000d, 3000d),
            new RoofPoint2D(0d, 3000d),
        };
        var start = new RoofPoint3D(3000d, 1500d, 0d);
        var end = new RoofPoint3D(9000d, 4500d, 0d);
        var oldLength = Length(start, end);
        var oldAngle = LocalAngle(start, end);

        Assert.True(TryMap(start, end, target, out var mappedStart, out var mappedEnd, out _));
        Assert.InRange(Length(mappedStart, mappedEnd), oldLength - 0.01, oldLength + 0.01);
        Assert.InRange(LocalAngle(mappedStart, mappedEnd), oldAngle - 1e-6, oldAngle + 1e-6);
    }

    [Fact]
    public void RepeatedResizeSequence_DoesNotAccumulateLengthDrift()
    {
        var start = new RoofPoint3D(2500d, 1500d, 0d);
        var end = new RoofPoint3D(7500d, 4500d, 0d);
        var originalLength = Length(start, end);
        var currentStart = start;
        var currentEnd = end;
        var footprints = new[]
        {
            WiderFootprint(12000d),
            WiderFootprint(7000d),
            WiderFootprint(15000d),
            new[]
            {
                new RoofPoint2D(0d, 0d),
                new RoofPoint2D(9000d, 0d),
                new RoofPoint2D(9000d, 8000d),
                new RoofPoint2D(0d, 8000d),
            },
            SourceFootprint,
        };

        var source = SourceFootprint;
        foreach (var target in footprints)
        {
            Assert.True(TryMap(currentStart, currentEnd, source, target, out currentStart, out currentEnd, out var metrics));
            Assert.True(metrics.GeometryPreserved);
            source = target;
        }

        Assert.InRange(Length(currentStart, currentEnd), originalLength - 0.01, originalLength + 0.01);
    }

    [Fact]
    public void MapSegment_RotatedBaseline_PreservesLengthAfterResize()
    {
        var start = new RoofPoint3D(1000d, 5000d, 0d);
        var end = new RoofPoint3D(9000d, 1000d, 0d);
        var oldLength = Length(start, end);

        Assert.True(TryMap(start, end, WiderFootprint(13000d), out var mappedStart, out var mappedEnd, out _));
        Assert.InRange(Length(mappedStart, mappedEnd), oldLength - 0.01, oldLength + 0.01);
    }

    [Fact]
    public void IndependentEndpointMapping_WouldHaveChangedLength_OnAspectChange()
    {
        var target = new[]
        {
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(20000d, 0d),
            new RoofPoint2D(20000d, 3000d),
            new RoofPoint2D(0d, 3000d),
        };
        var start = new RoofPoint3D(3000d, 1500d, 0d);
        var end = new RoofPoint3D(9000d, 4500d, 0d);
        var oldLength = Length(start, end);

        Assert.True(RoofAttachedManualPlacementRules.TryMapPoint(
            start,
            SourceFootprint,
            0d,
            PlaneNormal,
            target,
            0d,
            PlaneNormal,
            out var mappedStart));
        Assert.True(RoofAttachedManualPlacementRules.TryMapPoint(
            end,
            SourceFootprint,
            0d,
            PlaneNormal,
            target,
            0d,
            PlaneNormal,
            out var mappedEnd));

        var independentLength = Length(mappedStart, mappedEnd);
        Assert.True(Math.Abs(independentLength - oldLength) > 1d);
    }

    private static bool TryMap(
        RoofPoint3D start,
        RoofPoint3D end,
        RoofPoint2D[] target,
        out RoofPoint3D mappedStart,
        out RoofPoint3D mappedEnd,
        out RoofAttachedManualPlacementRules.RemapMetrics metrics) =>
        TryMap(start, end, SourceFootprint, target, out mappedStart, out mappedEnd, out metrics);

    private static bool TryMap(
        RoofPoint3D start,
        RoofPoint3D end,
        RoofPoint2D[] source,
        RoofPoint2D[] target,
        out RoofPoint3D mappedStart,
        out RoofPoint3D mappedEnd,
        out RoofAttachedManualPlacementRules.RemapMetrics metrics) =>
        RoofAttachedManualPlacementRules.TryMapSegment(
            start,
            end,
            source,
            0d,
            PlaneNormal,
            target,
            0d,
            PlaneNormal,
            out mappedStart,
            out mappedEnd,
            out metrics);

    private static RoofPoint2D[] WiderFootprint(double width) =>
    [
        new(0d, 0d),
        new(width, 0d),
        new(width, 6000d),
        new(0d, 6000d),
    ];

    private static double Length(RoofPoint3D start, RoofPoint3D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var dz = end.Z - start.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double LocalAngle(RoofPoint3D start, RoofPoint3D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Atan2(dy, dx);
    }
}
