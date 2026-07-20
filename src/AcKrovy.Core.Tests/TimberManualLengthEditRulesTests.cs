using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberManualLengthEditRulesTests
{
    [Theory]
    [InlineData(LengthCalculationMode.ManualLength, true)]
    [InlineData(LengthCalculationMode.PlanLength, false)]
    [InlineData(LengthCalculationMode.SlopeCorrected, false)]
    public void ExplicitMode_ControlsManualLengthAvailability(
        LengthCalculationMode mode,
        bool expected)
    {
        var data = Element(TimberElementType.Rafter, mode);

        Assert.Equal(expected, TimberManualLengthEditRules.CanEdit([data]));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, false)]
    [InlineData(TimberElementType.WallPlate, false)]
    [InlineData(TimberElementType.Purlin, false)]
    [InlineData(TimberElementType.Post, true)]
    [InlineData(TimberElementType.CollarTie, false)]
    [InlineData(TimberElementType.Brace, false)]
    [InlineData(TimberElementType.TieBeam, false)]
    public void AutomaticMode_UsesEffectiveModeForElementType(
        TimberElementType type,
        bool expected)
    {
        var data = Element(type, LengthCalculationMode.AutoByElementType);

        Assert.Equal(expected, TimberManualLengthEditRules.CanEdit([data]));
    }

    [Fact]
    public void BatchWithoutOverrides_RequiresEveryExistingModeToBeEffectivelyManual()
    {
        var elements = new[]
        {
            Element(TimberElementType.Post, LengthCalculationMode.AutoByElementType),
            Element(TimberElementType.Rafter, LengthCalculationMode.AutoByElementType),
        };

        Assert.False(TimberManualLengthEditRules.CanEdit(elements));
    }

    [Fact]
    public void CheckedManualModeOverride_EnablesBatchWithoutChangingSourceData()
    {
        var elements = new[]
        {
            Element(TimberElementType.Rafter, LengthCalculationMode.PlanLength),
            Element(TimberElementType.Brace, LengthCalculationMode.SlopeCorrected),
        };
        var original = elements.ToArray();

        var canEdit = TimberManualLengthEditRules.CanEdit(
            elements,
            lengthModeOverride: LengthCalculationMode.ManualLength);

        Assert.True(canEdit);
        Assert.Equal(original, elements);
    }

    [Fact]
    public void AutomaticModeWithCheckedPostTypeOverride_EnablesBatch()
    {
        var elements = new[]
        {
            Element(TimberElementType.Rafter, LengthCalculationMode.AutoByElementType),
            Element(TimberElementType.Brace, LengthCalculationMode.AutoByElementType),
        };

        Assert.True(TimberManualLengthEditRules.CanEdit(
            elements,
            elementTypeOverride: TimberElementType.Post));
    }

    private static TimberElementData Element(TimberElementType type, LengthCalculationMode mode) => new()
    {
        ElementType = type,
        LengthCalculationMode = mode,
        ManualLengthMm = 2500d,
    };
}
