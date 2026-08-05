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
    [InlineData(TimberFramedBlockContentKind.Plain, 0)]
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
    public void DimensionColumnLocalX_IsNegativeCanonicalForPlainAndFramed()
    {
        var plainX = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Plain,
                0d,
                2.5d);
        var framedX = TimberFramedBlockContentDefinitionRules
            .CalculateDimensionColumnLocalX(
                TimberFramedBlockContentKind.Circle,
                TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
                2.5d);

        Assert.True(plainX < 0d);
        Assert.True(framedX < plainX);
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
