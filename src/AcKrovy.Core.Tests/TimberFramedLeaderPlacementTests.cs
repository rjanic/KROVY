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
    public void FramedDefaultLayout_UsesSixtyDegreeFirstSegment(
        ItemNumberLeaderStyle style)
    {
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "K1",
            style,
            TimberLeaderHorizontalSide.Right);
        var angle = Math.Atan2(layout.KneeY, layout.KneeX) * 180d / Math.PI;

        Assert.Equal(60d, angle, 10);
        Assert.Equal(
            60d,
            TimberNativeLeaderStyleRules.FramedSettings.FirstSegmentAngleDegrees);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void StandaloneFramedLayout_UsesInsertionPointAsTerminalVertex(
        ItemNumberLeaderStyle style)
    {
        var layout = TimberItemLeaderLayoutCalculator.CalculateBlock(
            new TimberLeaderPlacement(0, 0, 0, 360, 0),
            "K1",
            style,
            TimberLeaderHorizontalSide.Right);

        Assert.Equal(layout.KneeX, layout.ContentX, 10);
        Assert.Equal(layout.KneeY, layout.ContentY, 10);
        Assert.Equal(
            0d,
            TimberItemLeaderLayoutCalculator.FramedItemLandingDistanceMm);
    }

    [Theory]
    [InlineData(0, 100, 300, 100, 150, 100)]
    [InlineData(0, 100, -300, 100, -150, 100)]
    [InlineData(20, 500, 320, 500, 170, 500)]
    [InlineData(20, -500, 320, -500, 170, -500)]
    [InlineData(50, 250, -250, 250, -100, 250)]
    public void CombinedDimensionsText_IsCenteredOnActualLandingEndpoints(
        double startX,
        double startY,
        double endX,
        double endY,
        double expectedX,
        double expectedY)
    {
        var position =
            TimberItemLeaderLayoutCalculator.CalculateSegmentMidpoint(
                startX,
                startY,
                endX,
                endY);

        Assert.Equal(expectedX, position.X, 10);
        Assert.Equal(expectedY, position.Y, 10);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void CombinedFramedLanding_UsesCentralizedThreeHundredFiftyMillimetres(
        ItemNumberLeaderStyle style)
    {
        Assert.NotEqual(ItemNumberLeaderStyle.Plain, style);
        Assert.Equal(
            350d,
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm);
        Assert.Equal(
            0d,
            TimberNativeLeaderStyleRules.FramedSettings.LandingDistance);
        Assert.Equal(
            350d,
            TimberNativeLeaderStyleRules.CombinedFramedSettings.LandingDistance);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void StandaloneFramedStyles_UseOriginalNativeContract(
        ItemNumberLeaderStyle style)
    {
        Assert.True(TimberNativeLeaderStyleRules.UsesSplineLeader(style));
        Assert.True(
            TimberNativeLeaderStyleRules.UsesInsertionPointBlockAttachment(style));
        Assert.False(TimberNativeLeaderStyleRules.FramedSettings.UsesStraightLeader);
        Assert.False(TimberNativeLeaderStyleRules.FramedSettings.HasArrowhead);
        Assert.False(TimberNativeLeaderStyleRules.FramedSettings.UsesAnnotationScale);
    }

    [Fact]
    public void CombinedFramedStyle_IsIndependentFromStandaloneContract()
    {
        Assert.True(
            TimberNativeLeaderStyleRules.CombinedFramedSettings.UsesStraightLeader);
        Assert.True(TimberNativeLeaderStyleRules.CombinedFramedSettings.HasArrowhead);
        Assert.Equal(
            60,
            TimberNativeLeaderStyleRules.CombinedFramedSettings
                .FirstSegmentAngleDegrees);
        Assert.NotEqual(
            TimberNativeLeaderStyleRules.FramedSettings.StyleName,
            TimberNativeLeaderStyleRules.CombinedFramedSettings.StyleName);
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
