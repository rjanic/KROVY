using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentWholeAnnotationHalfTurnRulesTests
{
    [Theory]
    [InlineData(90d)]
    [InlineData(-90d)]
    [InlineData(270d)]
    [InlineData(450d)]
    public void VerticalModuloDirections_RequireWholeAnnotationHalfTurn(
        double sourceDeg)
    {
        Assert.True(
            TimberFramedBlockContentWholeAnnotationHalfTurnRules
                .RequiresWholeAnnotationHalfTurn(sourceDeg * Math.PI / 180d));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(45d)]
    [InlineData(60d)]
    [InlineData(89d)]
    [InlineData(91d)]
    [InlineData(120d)]
    [InlineData(135d)]
    [InlineData(180d)]
    [InlineData(225d)]
    [InlineData(315d)]
    public void NonVerticalMatrix_NeverRequiresWholeAnnotationHalfTurn(
        double sourceDeg)
    {
        Assert.False(
            TimberFramedBlockContentWholeAnnotationHalfTurnRules
                .RequiresWholeAnnotationHalfTurn(sourceDeg * Math.PI / 180d));
    }

    [Theory]
    [InlineData(-90d)]
    [InlineData(90d)]
    public void NewOrLegacyVertical_TransitionsToAppliedExactlyOnce(
        double sourceDeg)
    {
        var first = Decide(sourceDeg, currentRevision: 2);
        Assert.True(first.Required);
        Assert.False(first.AppliedBefore);
        Assert.True(first.TransformRequired);
        Assert.True(first.AppliedAfter);
        Assert.Equal(3, first.RevisionAfter);

        var second = Decide(sourceDeg, first.RevisionAfter);
        Assert.True(second.Required);
        Assert.True(second.AppliedBefore);
        Assert.False(second.TransformRequired);
        Assert.True(second.AppliedAfter);
        Assert.Equal(3, second.RevisionAfter);
    }

    [Fact]
    public void NonVerticalToVertical_AppliesOnce()
    {
        var decision = Decide(90d, currentRevision: 2);
        Assert.True(decision.TransformRequired);
        Assert.Equal(3, decision.RevisionAfter);
    }

    [Fact]
    public void VerticalToNonVertical_RemovesOnce()
    {
        var first = Decide(45d, currentRevision: 3);
        Assert.True(first.AppliedBefore);
        Assert.False(first.Required);
        Assert.True(first.TransformRequired);
        Assert.False(first.AppliedAfter);
        Assert.Equal(2, first.RevisionAfter);

        var second = Decide(45d, first.RevisionAfter);
        Assert.False(second.TransformRequired);
        Assert.Equal(2, second.RevisionAfter);
    }

    [Fact]
    public void PositiveVerticalToNegativeVertical_DoesNotApplySecondHalfTurn()
    {
        var decision = Decide(-90d, currentRevision: 3);
        Assert.True(decision.Required);
        Assert.True(decision.AppliedBefore);
        Assert.False(decision.TransformRequired);
        Assert.True(decision.AppliedAfter);
        Assert.Equal(3, decision.RevisionAfter);
    }

    private static WholeAnnotationHalfTurnDecision Decide(
        double sourceDeg,
        int currentRevision) =>
        TimberFramedBlockContentWholeAnnotationHalfTurnRules.Decide(
            sourceDeg * Math.PI / 180d,
            currentRevision);
}
