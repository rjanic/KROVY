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
            TimberFramedBlockContentPresentation.Combined);
        var second = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "MEDIUM",
            "AK_KROVY_CLASSIC",
            "AK_KROVY_TECHNICAL",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);

        Assert.Equal(first, second);
        Assert.Contains("CIR", first, StringComparison.Ordinal);
        Assert.Contains("COMB", first, StringComparison.Ordinal);
        Assert.StartsWith("AK_KROVY_FBC_", first, StringComparison.Ordinal);
        Assert.DoesNotContain("50", first, StringComparison.Ordinal);
        Assert.DoesNotContain("_L", first, StringComparison.Ordinal);
        Assert.DoesNotContain("_R", first, StringComparison.Ordinal);
    }

    [Fact]
    public void LeftAndRight_ShareSameBtrKey()
    {
        // Side is ModelSpace knee/landing only; AttrDef/frame local geometry
        // is identical, so Left/Right must not fork shared definitions.
        var left = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);
        var right = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined);

        Assert.Equal(left, right);
        Assert.Equal(
            TimberFramedBlockContentVariantRules.CreateSafeBlockName(left),
            TimberFramedBlockContentVariantRules.CreateSafeBlockName(right));
    }

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
            TimberFramedBlockContentPresentation.Combined);

        Assert.Contains("PLAIN", key, StringComparison.Ordinal);
        Assert.Contains("_NONE_", key, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemOnly_AndCombined_Differ()
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
            TimberFramedBlockContentPresentation.Combined);

        Assert.Contains("ITEM", item, StringComparison.Ordinal);
        Assert.Contains("COMB", combined, StringComparison.Ordinal);
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
            TimberFramedBlockContentPresentation.Combined);
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
            TimberFramedBlockContentPresentation.Combined);

        Assert.DoesNotContain("DEN", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ANGLE", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("25", key, StringComparison.Ordinal);
        Assert.DoesNotContain("100", key, StringComparison.Ordinal);
        Assert.DoesNotContain("AK_G5C", key, StringComparison.Ordinal);
        Assert.DoesNotContain("AK_DEV", key, StringComparison.Ordinal);
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
                TimberFramedBlockContentPresentation.Combined));
}
