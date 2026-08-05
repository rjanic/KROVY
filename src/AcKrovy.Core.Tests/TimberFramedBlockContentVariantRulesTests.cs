using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentVariantRulesTests
{
    [Fact]
    public void CreateRawKey_IsDeterministicAndCultureInvariant()
    {
        var first = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "AK_KROVY_CLASSIC",
            "AK_KROVY_TECHNICAL",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var second = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "AK_KROVY_CLASSIC",
            "AK_KROVY_TECHNICAL",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        Assert.Equal(first, second);
        Assert.Contains("CIR", first, StringComparison.Ordinal);
        Assert.Contains("COMB", first, StringComparison.Ordinal);
        Assert.Contains("R2", first, StringComparison.Ordinal);
        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            first,
            StringComparison.Ordinal);
        Assert.StartsWith("AK_KROVY_FBC_R2_", first, StringComparison.Ordinal);
        Assert.DoesNotContain("50", first, StringComparison.Ordinal);
        Assert.DoesNotContain("_L_", first, StringComparison.Ordinal);
        Assert.DoesNotContain("_LEFT", first, StringComparison.Ordinal);
        Assert.DoesNotContain("_RIGHT", first, StringComparison.Ordinal);
    }

    [Fact]
    public void LeaderLeftAndRight_ShareSameColumnSideBtrKey()
    {
        // Leader Side is ModelSpace knee/landing only; same column side must
        // share one R2 Combined definition.
        var left = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var right = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        Assert.Equal(left, right);
        Assert.Equal(
            TimberFramedBlockContentVariantRules.CreateSafeBlockName(left),
            TimberFramedBlockContentVariantRules.CreateSafeBlockName(right));
    }

    [Fact]
    public void Combined_DimensionColumnSides_Differ()
    {
        var negative = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var positive = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);

        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            negative,
            StringComparison.Ordinal);
        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsPositiveXToken,
            positive,
            StringComparison.Ordinal);
        Assert.NotEqual(negative, positive);
    }

    [Fact]
    public void Combined_RequiresDimensionColumnSide() =>
        Assert.Throws<ArgumentNullException>(() =>
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Circle,
                "SMALL",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined));

    [Fact]
    public void Plain_ForcesNoneFrameSize()
    {
        var key = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Plain,
            "LARGE",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);

        Assert.Contains("PLAIN", key, StringComparison.Ordinal);
        Assert.Contains("_NONE_", key, StringComparison.Ordinal);
        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsPositiveXToken,
            key,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ItemOnly_OmitsDimensionColumnSideAndDiffersFromCombined()
    {
        var item = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            3.0d,
            2.5d,
            TimberFramedBlockContentPresentation.ItemOnly);
        var combined = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            3.0d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        Assert.Contains("ITEM", item, StringComparison.Ordinal);
        Assert.Contains("COMB", combined, StringComparison.Ordinal);
        Assert.Contains("R2", item, StringComparison.Ordinal);
        Assert.DoesNotContain(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            item,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TimberFramedBlockContentVariantRules.DimensionsPositiveXToken,
            item,
            StringComparison.Ordinal);
        Assert.NotEqual(item, combined);
    }

    [Fact]
    public void CreateSafeBlockName_TruncatesWithStableHash()
    {
        var raw = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Rectangle,
            "VERY_LONG_SIZE_TOKEN_FOR_TESTING",
            "A_VERY_LONG_ITEM_TEXT_STYLE_NAME_FOR_HASHING",
            "A_VERY_LONG_DIMENSION_TEXT_STYLE_NAME_FOR_HASHING",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var safe = TimberFramedBlockContentVariantRules.CreateSafeBlockName(raw, 31);
        Assert.True(safe.Length <= 31);
        Assert.Equal(safe, TimberFramedBlockContentVariantRules.CreateSafeBlockName(raw, 31));
    }

    [Fact]
    public void DenominatorAndAngle_AreAbsentFromKey()
    {
        var key = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);

        Assert.DoesNotContain("DEN", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ANGLE", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("25", key, StringComparison.Ordinal);
        Assert.DoesNotContain("100", key, StringComparison.Ordinal);
        Assert.DoesNotContain("AK_G5C", key, StringComparison.Ordinal);
        Assert.DoesNotContain("AK_DEV", key, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceHandle", key, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementId", key, StringComparison.Ordinal);
    }

    [Fact]
    public void OppositeDimensionColumnSide_Flips()
    {
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            TimberFramedBlockContentVariantRules.OppositeDimensionColumnSide(
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            TimberFramedBlockContentVariantRules.OppositeDimensionColumnSide(
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX));
    }

    [Fact]
    public void InvalidStyle_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Plain,
                "NONE",
                " ",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
}
