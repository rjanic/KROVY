using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberItemLeaderTextFitPolicyTests
{
    [Fact]
    public void ShortToken_SelectsSmallestFrameThatIncludesPadding()
    {
        var result = Evaluate("K1", SmallInnerWidth - 1d);

        Assert.True(result.Fits);
        Assert.Equal(TimberItemLeaderBlockSize.Small,
            result.EvaluatedDefinition.Size);
        Assert.Equal(SmallInnerWidth, result.AvailableInnerWidthMm, 6);
        Assert.Equal(TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm,
            result.HorizontalPaddingMm, 6);
    }

    [Theory]
    [InlineData("K1", 120d)]
    [InlineData("K99", 180d)]
    [InlineData("KL99", 240d)]
    [InlineData("VT99", 240d)]
    public void CommonProductionTokens_WithNormalMeasuredWidthsRemainSmall(
        string token,
        double measuredWidthMm)
    {
        foreach (var style in new[]
                 {
                     ItemNumberLeaderStyle.Circle,
                     ItemNumberLeaderStyle.Slot,
                     ItemNumberLeaderStyle.Rectangle,
                 })
        {
            var result = TimberItemLeaderBlockDefinitionRules
                .EvaluateMeasuredTextWidth(style, token, measuredWidthMm);

            Assert.True(result.Fits);
            Assert.Equal(token, result.ItemText);
            Assert.Equal(TimberItemLeaderBlockSize.Small,
                result.EvaluatedDefinition.Size);
            Assert.Equal(
                TimberItemLeaderBlockDefinitionRules
                    .BaseFramedItemTextHeightAtScale50Mm,
                result.EvaluatedDefinition.TextHeightMm);
            Assert.Equal(
                style == ItemNumberLeaderStyle.Circle
                    ? TimberItemLeaderBlockDefinitionRules.CircleDiameterMm
                    : TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm,
                result.EvaluatedDefinition.WidthMm);
        }
    }

    [Fact]
    public void MediumWidthToken_SelectsMedium()
    {
        var result = Evaluate("ABCDEFGH12", SmallInnerWidth + 1d);

        Assert.True(result.Fits);
        Assert.Equal(TimberItemLeaderBlockSize.Medium,
            result.EvaluatedDefinition.Size);
    }

    [Fact]
    public void LongerValidProductionToken_SelectsLarge()
    {
        const string token = "ABCDEFGH12345";
        Assert.Equal(12345,
            TimberElementIdentityRules.TryParseElementNumber(
                token,
                "ABCDEFGH"));

        var result = Evaluate(token, MediumInnerWidth + 1d);

        Assert.True(result.Fits);
        Assert.Equal(TimberItemLeaderBlockSize.Large,
            result.EvaluatedDefinition.Size);
    }

    [Fact]
    public void StyleSpecificWidthsCanSelectDifferentFramesForSameToken()
    {
        const string token = "ABCDEFGH99";
        var narrowStyle = Evaluate(token, SmallInnerWidth - 1d);
        var wideStyle = Evaluate(token, MediumInnerWidth + 1d);

        Assert.Equal(TimberItemLeaderBlockSize.Small,
            narrowStyle.EvaluatedDefinition.Size);
        Assert.Equal(TimberItemLeaderBlockSize.Large,
            wideStyle.EvaluatedDefinition.Size);
    }

    [Fact]
    public void WidthBeyondLargeReturnsExplicitOverflowWithoutChangingTokenOrHeight()
    {
        const string token = "ABCDEFGH2147483647";
        var result = Evaluate(token, LargeInnerWidth + 0.001d);

        Assert.False(result.Fits);
        Assert.Equal(token, result.ItemText);
        Assert.Equal(TimberItemLeaderBlockSize.Large,
            result.EvaluatedDefinition.Size);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .BaseFramedItemTextHeightAtScale50Mm,
            result.EvaluatedDefinition.TextHeightMm);
        Assert.Contains("exceeds", result.DiagnosticReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CircleAlsoRejectsMeasuredOverflowWithoutChangingGeometry()
    {
        var result = TimberItemLeaderBlockDefinitionRules
            .EvaluateMeasuredTextWidth(
                ItemNumberLeaderStyle.Circle,
                "K2147483647",
                CircleInnerWidth + 1d);

        Assert.False(result.Fits);
        Assert.Equal(TimberItemLeaderBlockSize.Small,
            result.EvaluatedDefinition.Size);
        Assert.Equal(TimberItemLeaderBlockDefinitionRules.CircleDiameterMm,
            result.EvaluatedDefinition.WidthMm);
    }

    private static TimberItemLeaderTextFitResult Evaluate(
        string token,
        double measuredWidthMm) =>
        TimberItemLeaderBlockDefinitionRules.EvaluateMeasuredTextWidth(
            ItemNumberLeaderStyle.Rectangle,
            token,
            measuredWidthMm);

    private static double SmallInnerWidth =>
        TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm -
        2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;

    private static double MediumInnerWidth =>
        TimberItemLeaderBlockDefinitionRules.MediumFrameWidthMm -
        2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;

    private static double LargeInnerWidth =>
        TimberItemLeaderBlockDefinitionRules.LargeFrameWidthMm -
        2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;

    private static double CircleInnerWidth =>
        TimberItemLeaderBlockDefinitionRules.CircleDiameterMm -
        2d * TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;
}
