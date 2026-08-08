using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentStretchNormalizeRulesTests
{
    [Fact]
    public void R2Parser_AcceptsCombinedDimnxAndDimpx()
    {
        var negative = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
        var positive = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                TimberFramedBlockContentKind.Slot,
                "SMALL",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX));

        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                negative,
                out var negParse));
        Assert.True(negParse.IsP3R2CombinedTarget);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            negParse.DimensionColumnSide);

        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                positive,
                out var posParse));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            posParse.DimensionColumnSide);
    }

    [Fact]
    public void R2Parser_RejectsItemOnlyForeignLegacyAndPartialSubstrings()
    {
        var itemOnly = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.ItemOnly));

        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                itemOnly,
                out var itemParse));
        Assert.True(itemParse.IsItemOnly);
        Assert.False(itemParse.IsP3R2CombinedTarget);
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                itemOnly,
                hasItemNo: true,
                hasWidth: false,
                hasHeight: false));

        Assert.False(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                "AK_G4_COMPOSITE_THING",
                out _));
        Assert.False(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                "SOME_DIMNX_NAME",
                out _));
        Assert.False(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                "AK_KROVY_FBC_R1_CIR_MEDIUM_COMB",
                out _));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                "FOREIGN_BLOCK",
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
    }

    [Fact]
    public void Filter_RequiresCombinedAttributeContract()
    {
        var combined = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateLegacyR2RawKey(
                TimberFramedBlockContentKind.Rectangle,
                "LARGE",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));

        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                combined,
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
                combined,
                hasItemNo: true,
                hasWidth: true,
                hasHeight: false));
    }

    [Fact]
    public void OperationOrder_IsDoglegThenContentSide()
    {
        Assert.Equal(
            new[]
            {
                TimberFramedBlockContentStretchNormalizeRules.DoglegStep,
                TimberFramedBlockContentStretchNormalizeRules.ContentSideStep,
            },
            TimberFramedBlockContentStretchNormalizeRules.NormalizeOperationOrder);
    }

    [Fact]
    public void ContentSide_NoOpAndOppositeSwap()
    {
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                TimberFramedBlockContentDimensionColumnMirrorDecision.Swap));
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(
                TimberFramedBlockContentDimensionColumnMirrorDecision.Swap));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(
                TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp));

        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX,
            TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                TimberFramedBlockContentDimensionColumnSide.NegativeLocalX));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX,
            TimberFramedBlockContentStretchNormalizeRules.OppositeColumnSide(
                TimberFramedBlockContentDimensionColumnSide.PositiveLocalX));
    }

    [Fact]
    public void SecondEvaluation_UnchangedContract()
    {
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.IsSecondEvaluationUnchanged(
                firstChanged: true,
                secondChanged: false));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsSecondEvaluationUnchanged(
                firstChanged: true,
                secondChanged: true));
    }

    [Fact]
    public void Automation_RequiresProofAndConfirmedCommand()
    {
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.ShouldRunAutomation(
                proofEnabled: false,
                globalCommandName: "STRETCH",
                confirmedCommandNames: ["STRETCH"]));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.ShouldRunAutomation(
                proofEnabled: true,
                globalCommandName: "STRETCH",
                confirmedCommandNames: []));
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.ShouldRunAutomation(
                proofEnabled: true,
                globalCommandName: "._STRETCH",
                confirmedCommandNames: ["stretch"]));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.ShouldRunAutomation(
                proofEnabled: true,
                globalCommandName: "MOVE",
                confirmedCommandNames: ["STRETCH"]));
    }
}
