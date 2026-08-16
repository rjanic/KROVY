using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayRebuildEraseRulesTests
{
    [Fact]
    public void InspectedChild_ErasesWheneverDisplayStoreExists_WithoutOwnerGate()
    {
        Assert.True(RoofDisplayRebuildEraseRules.ShouldEraseInspectedDisplayChild(true));
        Assert.False(RoofDisplayRebuildEraseRules.ShouldEraseInspectedDisplayChild(false));
    }

    [Theory]
    [InlineData("292E", "292E", true)]
    [InlineData("292e", "292E", true)]
    [InlineData("291A", "292E", false)]
    [InlineData(null, "292E", false)]
    [InlineData("292E", "", false)]
    public void OwnerMatchedSweep_UsesEffectiveOwnerOnly(
        string? effectiveOwner,
        string rebuildOwner,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofDisplayRebuildEraseRules.ShouldEraseOwnerMatchedSweepChild(
                displayStoreExists: true,
                effectiveOwner,
                rebuildOwner));
        Assert.False(
            RoofDisplayRebuildEraseRules.ShouldEraseOwnerMatchedSweepChild(
                displayStoreExists: false,
                effectiveOwner,
                rebuildOwner));
    }
}
