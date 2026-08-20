using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayGroupSelectabilityRulesTests
{
    [Fact]
    public void Locked_EnablesGroupSelection()
    {
        Assert.True(RoofDisplayGroupSelectabilityRules.ShouldEnableGroupSelection(RoofEditState.Locked));
    }

    [Fact]
    public void Unlocked_DisablesGroupSelection()
    {
        Assert.False(RoofDisplayGroupSelectabilityRules.ShouldEnableGroupSelection(RoofEditState.Unlocked));
    }

    [Fact]
    public void DefaultEditState_IsLocked()
    {
        Assert.Equal(RoofEditState.Locked, default(RoofEditState));
    }
}
