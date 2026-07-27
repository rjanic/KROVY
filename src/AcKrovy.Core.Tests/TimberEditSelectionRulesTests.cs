using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberEditSelectionRulesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(32)]
    public void ValidImpliedItems_AreAllUsedWithoutManualFallback(int count)
    {
        var implied = Enumerable.Range(1, count).ToArray();

        var decision = TimberEditSelectionRules.Evaluate(
            implied,
            _ => true);

        Assert.True(decision.UseImpliedSelection);
        Assert.Equal(implied, decision.ValidItems);
        Assert.Equal(0, decision.RejectedItems);
    }

    [Fact]
    public void EmptyImpliedSelection_RequestsManualFallback()
    {
        var decision = TimberEditSelectionRules.Evaluate(
            Array.Empty<int>(),
            _ => true);

        Assert.False(decision.UseImpliedSelection);
        Assert.Empty(decision.ValidItems);
        Assert.Equal(0, decision.RejectedItems);
    }

    [Fact]
    public void ImpliedSelectionWithoutSmartElements_RequestsManualFallback()
    {
        var decision = TimberEditSelectionRules.Evaluate(
            new[] { 1, 2, 3 },
            _ => false);

        Assert.False(decision.UseImpliedSelection);
        Assert.Empty(decision.ValidItems);
        Assert.Equal(3, decision.RejectedItems);
    }

    [Fact]
    public void MixedImpliedSelection_UsesEveryValidItemAndRejectsTheRest()
    {
        var decision = TimberEditSelectionRules.Evaluate(
            Enumerable.Range(1, 9).ToArray(),
            item => item is 1 or 3 or 5 or 7 or 9);

        Assert.True(decision.UseImpliedSelection);
        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, decision.ValidItems);
        Assert.Equal(4, decision.RejectedItems);
    }
}
