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
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var second = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "AK_KROVY_CLASSIC",
            "AK_KROVY_TECHNICAL",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);

        Assert.Equal(first, second);
        Assert.Contains("CIR", first, StringComparison.Ordinal);
        Assert.Contains("COMB", first, StringComparison.Ordinal);
        Assert.Contains("R3", first, StringComparison.Ordinal);
        Assert.Contains("RIGHT", first, StringComparison.Ordinal);
        Assert.DoesNotContain(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            first,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TimberFramedBlockContentVariantRules.DimensionsPositiveXToken,
            first,
            StringComparison.Ordinal);
        Assert.StartsWith("AK_KROVY_FBC_R3_", first, StringComparison.Ordinal);
        Assert.DoesNotContain("50", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Combined_RightAndLeft_AreDistinctKeys()
    {
        var left = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        var right = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide);
        var omitted = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);

        Assert.NotEqual(left, right);
        Assert.Equal(right, omitted);
        Assert.Contains("_LEFT_", left, StringComparison.Ordinal);
        Assert.Contains("_RIGHT_", right, StringComparison.Ordinal);
    }

    [Fact]
    public void Combined_DefaultsToRightContentVariant()
    {
        var key = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);
        Assert.True(TimberFramedBlockContentVariantRules.IsProductionR3Combined(key));
        Assert.True(
            TimberFramedBlockContentVariantRules.IsProductionR3CombinedContentVariant(
                key));
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                key,
                out var parse));
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide,
            parse.ContentVariantSide);
        Assert.Equal(TimberFramedBlockContentKind.Circle, parse.ContentKind);
        Assert.True(parse.IsProductionCombinedContentIdentity);
    }

    [Theory]
    [InlineData(TimberFramedBlockContentKind.Circle, "CIR")]
    [InlineData(TimberFramedBlockContentKind.Rectangle, "REC")]
    [InlineData(TimberFramedBlockContentKind.Slot, "SLT")]
    public void TryParseR3VariantKey_ParsesContentKind(
        TimberFramedBlockContentKind kind,
        string kindToken)
    {
        var key = TimberFramedBlockContentVariantRules.CreateRawKey(
            kind,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide);
        Assert.Contains(kindToken, key, StringComparison.Ordinal);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                key,
                out var parse));
        Assert.Equal(kind, parse.ContentKind);
        Assert.Equal(
            TimberFramedCombinedG5ContentVariantRules.LeftColumnSide,
            parse.ContentVariantSide);
    }

    [Fact]
    public void ItemOnly_OmitsPresentationAmbiguity()
    {
        var item = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.ItemOnly);
        Assert.Contains("ITEM", item, StringComparison.Ordinal);
        Assert.DoesNotContain("COMB", item, StringComparison.Ordinal);
        Assert.DoesNotContain("RIGHT", item, StringComparison.Ordinal);
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR3VariantKey(
                item,
                out var isCombined,
                out var isItemOnly));
        Assert.False(isCombined);
        Assert.True(isItemOnly);
    }

    [Fact]
    public void LegacyR2_StillParsesForDebugStretchNormalize()
    {
        const string r2 =
            "AK_KROVY_FBC_R2_CIR_SMALL_COMB_DIMNX_I2.7_D2.5_ISSTANDARD_DSSTANDARD";
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(r2, out var parse));
        Assert.True(parse.IsP3R2CombinedTarget);
        Assert.True(
            TimberFramedBlockContentVariantRules.IsP3R2CombinedStretchNormalizeTarget(r2));
        Assert.False(
            TimberFramedBlockContentVariantRules.IsProductionR3Combined(r2));
    }

    [Fact]
    public void CreateSafeBlockName_TruncatesWithStableHash()
    {
        var longKey = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Rectangle,
            "LARGE",
            new string('A', 80),
            new string('B', 80),
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);
        var safe = TimberFramedBlockContentVariantRules.CreateSafeBlockName(longKey, 64);
        Assert.True(safe.Length <= 64);
    }
}
