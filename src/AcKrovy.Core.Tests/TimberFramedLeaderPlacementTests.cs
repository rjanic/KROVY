using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedLeaderPlacementTests
{
    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedDefaultLayout_ExtendsFirstSegmentByCentralizedAdditionalOffset(
        ItemNumberLeaderStyle style)
    {
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(100, 200, 500, 600, 0),
            "K1",
            style,
            TimberLeaderHorizontalSide.Right);

        var run = Math.Sqrt(
            Math.Pow(layout.KneeX - layout.AnchorX, 2) +
            Math.Pow(layout.KneeY - layout.AnchorY, 2));

        Assert.Equal(
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm + 350d,
            run,
            10);
        Assert.Equal(
            350d,
            TimberItemLeaderLayoutCalculator.FramedLeaderAdditionalOffsetMm);
        Assert.Equal(
            100d,
            run -
                (TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm + 250d),
            10);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedDefaultLayout_UsesFortyDegreeFirstSegment(
        ItemNumberLeaderStyle style)
    {
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "K1",
            style,
            TimberLeaderHorizontalSide.Right);
        var angle = Math.Atan2(layout.KneeY, layout.KneeX) * 180d / Math.PI;

        Assert.Equal(40d, angle, 10);
        Assert.Equal(
            40d,
            TimberNativeLeaderStyleRules.FramedSettings.FirstSegmentAngleDegrees);
    }

    [Fact]
    public void PlainDefaultLayout_DoesNotUseFramedAdditionalOffset()
    {
        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "K1",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            DistanceToKnee(layout),
            10);
    }

    [Fact]
    public void DimensionsLeaderLayout_DoesNotUseFramedAdditionalOffset()
    {
        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "160x200",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm,
            DistanceToKnee(layout),
            10);
    }

    [Fact]
    public void AnnotationStretch_IsCapturedAndSurvivesReconcile()
    {
        var captured = TimberFramedLeaderManualOffsetCalculator.Capture(
            TimberFramedLeaderManualOffset.Zero,
            100,
            200,
            175,
            260,
            0);

        var reconciled = TimberFramedLeaderManualOffsetCalculator.Apply(
            captured,
            100,
            200,
            0);

        Assert.Equal((175d, 260d), reconciled);
    }

    [Fact]
    public void RenumberOrMetadataRefresh_PreservesPersistedManualOffset()
    {
        var offset = new TimberFramedLeaderManualOffset(75, -40);

        var refreshed = TimberFramedLeaderManualOffsetCalculator.Apply(
            offset,
            500,
            700,
            0);

        Assert.Equal((575d, 660d), refreshed);
    }

    [Fact]
    public void SourceGeometryChange_RebasesPersistedOffsetOnNewAutomaticPosition()
    {
        var offset = new TimberFramedLeaderManualOffset(75, -40);

        var moved = TimberFramedLeaderManualOffsetCalculator.Apply(
            offset,
            1500,
            1700,
            0);

        Assert.Equal((1575d, 1660d), moved);
    }

    [Fact]
    public void SourceRotation_RotatesOffsetInLocalAnnotationPlane()
    {
        var offset = new TimberFramedLeaderManualOffset(100, 50);

        var rotated = TimberFramedLeaderManualOffsetCalculator.Apply(
            offset,
            1000,
            2000,
            Math.PI / 2d);

        Assert.Equal(950d, rotated.X, 10);
        Assert.Equal(2100d, rotated.Y, 10);
    }

    [Fact]
    public void PersistedOffset_IsPortableAcrossCopyAndSerializationBoundaries()
    {
        var copied = new TimberFramedLeaderManualOffset(125, -35);

        var result = TimberFramedLeaderManualOffsetCalculator.Apply(
            copied,
            300,
            400,
            0);

        Assert.Equal((425d, 365d), result);
    }

    private static double DistanceToKnee(TimberItemLeaderLayout layout) =>
        Math.Sqrt(
            Math.Pow(layout.KneeX - layout.AnchorX, 2) +
            Math.Pow(layout.KneeY - layout.AnchorY, 2));
}
