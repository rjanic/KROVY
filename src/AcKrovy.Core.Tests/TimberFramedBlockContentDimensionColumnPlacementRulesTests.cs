using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentDimensionColumnPlacementRulesTests
{
    private static readonly TimberPlanarPoint Knee = new(0d, 0d);
    private static readonly TimberPlanarPoint Item = new(550d, 0d);

    [Fact]
    public void CorrectColumn_BetweenKneeAndItem_IsCorrect()
    {
        // Handle-style landing: column toward knee from ITEM_NO.
        var width = new TimberPlanarPoint(450d, 10d);
        var height = new TimberPlanarPoint(450d, -10d);
        var evaluation =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateDimensionColumnPlacement(Knee, Item, width, height);

        Assert.True(evaluation.IsCorrect);
        Assert.InRange(evaluation.ParameterT, 0.01d, 0.99d);
        Assert.True(
            evaluation.PerpendicularDistance <=
            TimberFramedBlockContentDimensionColumnPlacementRules
                .DefaultColumnPerpendicularToleranceMm);
        Assert.Equal(450d, evaluation.DimensionColumnCenter.X, 9);
        Assert.Equal(0d, evaluation.DimensionColumnCenter.Y, 9);
    }

    [Fact]
    public void WrongColumn_PastItem_IsIncorrect_MirrorCorrect()
    {
        var width = new TimberPlanarPoint(650d, 10d);
        var height = new TimberPlanarPoint(650d, -10d);
        var mirror =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(Knee, Item, width, height);

        Assert.False(mirror.Current.IsCorrect);
        Assert.True(mirror.Current.ParameterT > 1d);
        Assert.True(mirror.Mirrored.IsCorrect);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.Swap,
            mirror.Decision);
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(
                mirror.Decision));
        Assert.False(
            TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                mirror.Decision));
        Assert.Equal(450d, mirror.MirroredDimensionColumnCenter.X, 9);
    }

    [Fact]
    public void VisuallyCorrect_LikeHandle2928_IsNoOp()
    {
        // Exact-90° vertical landing: knee below ITEM_NO, column between.
        var knee = new TimberPlanarPoint(19686.62d, 5255.10d);
        var item = new TimberPlanarPoint(19686.62d, 5805.10d);
        var width = new TimberPlanarPoint(19686.62d, 5605.10d);
        var height = new TimberPlanarPoint(19686.62d, 5555.10d);
        var mirror =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(knee, item, width, height);

        Assert.True(mirror.Current.IsCorrect);
        Assert.False(mirror.Mirrored.IsCorrect);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp,
            mirror.Decision);
        Assert.True(
            TimberFramedBlockContentStretchNormalizeRules.IsContentSideNoOp(
                mirror.Decision));
        Assert.Equal(
            "no-op",
            TimberFramedBlockContentDimensionColumnPlacementRules.DescribeDecision(
                mirror.Decision));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(270d)]
    public void CardinalAngles_CorrectAndWrong_DecideConsistently(double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var axis = TimberPlanarVector.FromAngleRadians(radians);
        var knee = new TimberPlanarPoint(0d, 0d);
        var item = new TimberPlanarPoint(550d * axis.X, 550d * axis.Y);
        var towardKnee = new TimberPlanarPoint(450d * axis.X, 450d * axis.Y);
        var pastItem = new TimberPlanarPoint(650d * axis.X, 650d * axis.Y);

        var correct =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(
                    knee,
                    item,
                    towardKnee,
                    towardKnee);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp,
            correct.Decision);

        var wrong =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(
                    knee,
                    item,
                    pastItem,
                    pastItem);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.Swap,
            wrong.Decision);

        // After conceptual swap, mirrored of wrong is the correct placement.
        var afterSwap =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(
                    knee,
                    item,
                    wrong.MirroredDimensionColumnCenter,
                    wrong.MirroredDimensionColumnCenter);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp,
            afterSwap.Decision);
    }

    [Fact]
    public void DegenerateKneeToItem_Fails()
    {
        var evaluation =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(
                    new TimberPlanarPoint(10d, 10d),
                    new TimberPlanarPoint(10d, 10d),
                    new TimberPlanarPoint(5d, 10d),
                    new TimberPlanarPoint(5d, 10d));
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.FailDegenerate,
            evaluation.Decision);
        Assert.False(evaluation.Current.IsCorrect);
    }

    [Fact]
    public void FarOffAxis_BothIncorrect_Unresolved()
    {
        var width = new TimberPlanarPoint(275d, 200d);
        var height = new TimberPlanarPoint(275d, 220d);
        var mirror =
            TimberFramedBlockContentDimensionColumnPlacementRules
                .EvaluateMirroredDimensionColumnPlacement(Knee, Item, width, height);
        Assert.False(mirror.Current.IsCorrect);
        Assert.False(mirror.Mirrored.IsCorrect);
        Assert.Equal(
            TimberFramedBlockContentDimensionColumnMirrorDecision.FailUnresolved,
            mirror.Decision);
    }
}
