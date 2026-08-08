using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentDefinitionRulesTests
{
    [Theory]
    [InlineData(TimberFramedBlockContentPresentation.Combined, 3)]
    [InlineData(TimberFramedBlockContentPresentation.ItemOnly, 1)]
    public void ExpectedAttributeCount_MatchesPresentation(
        TimberFramedBlockContentPresentation presentation,
        int expected) =>
        Assert.Equal(
            expected,
            TimberFramedBlockContentDefinitionRules.ExpectedAttributeCount(
                presentation));

    [Theory]
    [InlineData(TimberFramedBlockContentKind.Plain, 1)]
    [InlineData(TimberFramedBlockContentKind.Circle, 1)]
    [InlineData(TimberFramedBlockContentKind.Rectangle, 1)]
    [InlineData(TimberFramedBlockContentKind.Slot, 1)]
    public void ExpectedFrameEntityCount_MatchesKind(
        TimberFramedBlockContentKind kind,
        int expected) =>
        Assert.Equal(
            expected,
            TimberFramedBlockContentDefinitionRules.ExpectedFrameEntityCount(kind));

    [Fact]
    public void CombinedTags_AreExactItemWidthHeight()
    {
        var tags = TimberFramedBlockContentDefinitionRules.ExpectedAttributeTags(
            TimberFramedBlockContentPresentation.Combined);

        Assert.Equal(
            new[]
            {
                TimberFramedBlockContentDefinitionRules.ItemNoTag,
                TimberFramedBlockContentDefinitionRules.WidthTag,
                TimberFramedBlockContentDefinitionRules.HeightTag,
            },
            tags);
    }

    [Fact]
    public void ItemOnlyTags_AreExactItemNoOnly()
    {
        var tags = TimberFramedBlockContentDefinitionRules.ExpectedAttributeTags(
            TimberFramedBlockContentPresentation.ItemOnly);

        Assert.Equal(
            new[] { TimberFramedBlockContentDefinitionRules.ItemNoTag },
            tags);
    }

    [Fact]
    public void PlainItemOnly_IsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            TimberFramedBlockContentDefinitionRules.ValidateRequest(
                TimberFramedBlockContentKind.Plain,
                TimberFramedBlockContentPresentation.ItemOnly));

    [Fact]
    public void AttrDefHeights_UseBaselineDenominatorNotRuntimeDenom()
    {
        var item = TimberFramedBlockContentDefinitionRules
            .CalculateBaselineItemModelHeightMm(2.7d);
        var dim = TimberFramedBlockContentDefinitionRules
            .CalculateBaselineDimensionModelHeightMm(2.5d);

        Assert.Equal(2.7d * 50d, item);
        Assert.Equal(2.5d * 50d, dim);
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberFramedBlockContentDefinitionRules.BaselineDenominator);
    }

    [Fact]
    public void WidthHeightLocalY_StraddleLandingAtBaseline()
    {
        var widthY = TimberFramedBlockContentDefinitionRules.CalculateWidthLocalY(2.5d);
        var heightY =
            TimberFramedBlockContentDefinitionRules.CalculateHeightLocalY(2.5d);

        Assert.True(widthY > 0d);
        Assert.True(heightY < 0d);
        Assert.Equal(widthY, -heightY, 1e-9);
    }

    [Fact]
    public void DimensionColumnLocalX_RightNegative_LeftPositiveMirror()
    {
        var plainNeg = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Plain,
                0d,
                2.5d,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var plainPos = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Plain,
                0d,
                2.5d,
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);
        var framedNeg = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                2.5d,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var framedPos = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                2.5d,
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);

        // PositiveLocalX = +offset; NegativeLocalX = −offset (literal enum bake).
        // R3_RIGHT = NegativeLocalX (PASS); R3_LEFT = PositiveLocalX.
        Assert.True(plainPos > 0d);
        Assert.True(plainNeg < 0d);
        Assert.Equal(-plainPos, plainNeg, 1e-9);
        Assert.True(framedPos > 0d);
        Assert.True(framedNeg < 0d);
        Assert.Equal(-framedPos, framedNeg, 1e-9);
        Assert.True(Math.Abs(framedPos) > Math.Abs(plainPos));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.True(
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                2.5d,
                TimberFramedCombinedG5ContentVariantRules.RightColumnSide) < 0d);
        Assert.True(
            TimberFramedBlockContentDefinitionRules.CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                2.5d,
                TimberFramedCombinedG5ContentVariantRules.LeftColumnSide) > 0d);
    }

    [Theory]
    [InlineData(1d, TimberFramedBlockContentDimensionColumnSide.NegativeLocalX)]
    [InlineData(-1d, TimberFramedBlockContentDimensionColumnSide.PositiveLocalX)]
    public void ResolveDimensionColumnSide_FromContentLocalX(
        double contentLocalX,
        TimberFramedBlockContentDimensionColumnSide expected) =>
        Assert.Equal(
            expected,
            TimberFramedBlockContentDefinitionRules
                .ResolveDimensionColumnSideFromContentLocalX(contentLocalX));

    [Fact]
    public void TryClassifyDimensionColumnSide_FromAttrDefX()
    {
        Assert.True(
            TimberFramedBlockContentDefinitionRules.TryClassifyDimensionColumnSide(
                -430d,
                out var negative));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            negative);
        Assert.True(
            TimberFramedBlockContentDefinitionRules.TryClassifyDimensionColumnSide(
                430d,
                out var positive));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            positive);
        Assert.False(
            TimberFramedBlockContentDefinitionRules.TryClassifyDimensionColumnSide(
                0d,
                out _));
    }

    [Fact]
    public void FrameSizeToken_MapsProductionSizes()
    {
        Assert.Equal(
            "NONE",
            TimberFramedBlockContentDefinitionRules.GetFrameSizeToken(
                TimberFramedBlockContentKind.Plain,
                null));
        Assert.Equal(
            "SMALL",
            TimberFramedBlockContentDefinitionRules.GetFrameSizeToken(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockSize.Small));
        Assert.Equal(
            "MEDIUM",
            TimberFramedBlockContentDefinitionRules.GetFrameSizeToken(
                TimberFramedBlockContentKind.Slot,
                TimberItemLeaderBlockSize.Medium));
        Assert.Equal(
            "LARGE",
            TimberFramedBlockContentDefinitionRules.GetFrameSizeToken(
                TimberFramedBlockContentKind.Rectangle,
                TimberItemLeaderBlockSize.Large));
    }
}
