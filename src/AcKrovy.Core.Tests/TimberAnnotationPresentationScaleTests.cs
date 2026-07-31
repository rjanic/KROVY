using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationPresentationScaleTests
{
    [Theory]
    [InlineData(25, 62.5d)]
    [InlineData(50, 125d)]
    [InlineData(100, 250d)]
    public void FullLabelTextHeight_ScalesFromOneToFiftyExactlyOnce(
        int denominator,
        double expected) =>
        Assert.Equal(
            expected,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                Context(denominator).ScaleFactor));

    [Theory]
    [InlineData(25, 52.083333333333336d)]
    [InlineData(50, 104.16666666666667d)]
    [InlineData(100, 208.33333333333334d)]
    public void FullLabelThreeLineCenterOffset_DerivesFromScaledTextHeight(
        int denominator,
        double expected)
    {
        var textHeightMm =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                Context(denominator).ScaleFactor);

        Assert.Equal(
            expected,
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(textHeightMm),
            10);
    }

    [Theory]
    [InlineData(25, 62.5d)]
    [InlineData(50, 125d)]
    [InlineData(100, 250d)]
    public void DimensionsLeaderTextHeight_ScalesFromOneToFiftyExactlyOnce(
        int denominator,
        double expected) =>
        Assert.Equal(
            expected,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                Context(denominator).ScaleFactor));

    [Theory]
    [InlineData(25, 67.5d)]
    [InlineData(50, 135d)]
    [InlineData(100, 270d)]
    public void ItemNumberTextHeight_ScalesFromOneToFiftyExactlyOnce(
        int denominator,
        double expected) =>
        Assert.Equal(
            expected,
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                Context(denominator).ScaleFactor));

    [Theory]
    [InlineData(0.08d, 25, 0.04d)]
    [InlineData(0.08d, 50, 0.08d)]
    [InlineData(0.08d, 100, 0.16d)]
    [InlineData(350d, 25, 175d)]
    [InlineData(350d, 50, 350d)]
    [InlineData(350d, 100, 700d)]
    public void NativeLeaderNonZeroArrowOrLandingValue_ScalesExactlyOnce(
        double baseValue,
        int denominator,
        double expected) =>
        Assert.Equal(expected, Context(denominator).ScaleLength(baseValue), 10);

    [Fact]
    public void NativeLeaderZeroLandingValue_RemainsZero() =>
        Assert.Equal(0d, Context(100).ScaleLength(0d));

    [Fact]
    public void ScaleFactor_IsNotAppliedTwice()
    {
        var context = Context(100);

        Assert.Equal(
            250d,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor));
        Assert.NotEqual(
            500d,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor));
    }

    [Theory]
    [InlineData(25, 90d, 180d)]
    [InlineData(50, 180d, 360d)]
    [InlineData(100, 360d, 720d)]
    public void PlainItemNumberLeaderLayout_ScalesAutomaticClearanceExactlyOnce(
        int denominator,
        double expectedKneeX,
        double expectedContentY)
    {
        var placement = new TimberLeaderPlacement(0d, 0d, 0d, 0d, 0d);

        var layout = TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
            placement,
            "A1",
            TimberLeaderHorizontalSide.Right,
            Context(denominator).ScaleFactor);

        Assert.Equal(expectedKneeX, layout.KneeX, 8);
        Assert.Equal(expectedContentY, layout.ContentY, 8);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(37d)]
    public void PlainItemNumberLeader_TextEnvelopeClearsSourceAxis(
        double sourceAngleDegrees)
    {
        var rotation = sourceAngleDegrees * Math.PI / 180d;
        var normalX = -Math.Sin(rotation);
        var normalY = Math.Cos(rotation);
        var placement = new TimberLeaderPlacement(
            0d,
            0d,
            normalX *
                TimberItemNumberTypographyRules
                    .PlainItemNumberTextCenterOffsetAtScale50Mm,
            normalY *
                TimberItemNumberTypographyRules
                    .PlainItemNumberTextCenterOffsetAtScale50Mm,
            rotation);

        var layout =
            TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
                placement,
                "VT1",
                TimberLeaderHorizontalSide.Right,
                presentationScaleFactor: 1d);
        var centerDistanceFromSource =
            Math.Abs(layout.ContentX * normalX + layout.ContentY * normalY);
        var projectedHalfEnvelope =
            Math.Abs(normalX) * layout.EnvelopeWidthMm / 2d +
            Math.Abs(normalY) * layout.EnvelopeHeightMm / 2d;

        Assert.Equal(
            TimberItemNumberTypographyRules
                .PlainItemNumberTextClearanceAtScale50Mm,
            centerDistanceFromSource - projectedHalfEnvelope,
            8);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void FramedBlockDefinitions_RemainAtBaseDimensions(
        ItemNumberLeaderStyle style)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, "A1");

        Assert.Equal(1d, TimberItemLeaderBlockDefinitionRules.BlockScale);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .BaseFramedItemTextHeightAtScale50Mm,
            definition.TextHeightMm);
        Assert.True(
            definition.WidthMm >=
            TimberItemLeaderBlockDefinitionRules.CircleDiameterMm);
        Assert.True(
            definition.HeightMm >=
            TimberItemLeaderBlockDefinitionRules.FrameHeightMm);
    }

    [Fact]
    public void NoAnnotationsPlanner_RemainsEmpty()
    {
        var plan = TimberAnnotationRefreshPlanner.Create(new TimberElementData
        {
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
        });

        Assert.False(plan.EnsureLabel);
        Assert.False(plan.ReconcileSlopeArrow);
        Assert.False(plan.ReconcileSlopeAngleText);
    }

    private static TimberAnnotationScaleContext Context(int denominator) =>
        new(denominator, TimberAnnotationScaleSource.Drawing);
}
