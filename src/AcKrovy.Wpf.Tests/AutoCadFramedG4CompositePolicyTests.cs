using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedG4CompositePolicyTests
{
    [Fact]
    public void UsesG4Composite_ForStandaloneAndCombinedFramedRoles()
    {
        Assert.True(AutoCadFramedG4CompositePolicy.UsesG4Composite(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary));
        Assert.True(AutoCadFramedG4CompositePolicy.UsesG4Composite(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Slot,
            TimberMainAnnotationComponentRole.FramedItem));
        Assert.False(AutoCadFramedG4CompositePolicy.UsesG4Composite(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary));
    }

    [Fact]
    public void HeightContract_UsesPaperTimesEffectiveDenominator()
    {
        Assert.Equal(
            135d,
            AutoCadFramedG4CompositePolicy.CalculateItemCodeModelHeightMm(2.7d, 50));
        Assert.Equal(
            175d,
            AutoCadFramedG4CompositePolicy.CalculateItemCodeModelHeightMm(3.5d, 50));
        Assert.Equal(
            270d,
            AutoCadFramedG4CompositePolicy.CalculateItemCodeModelHeightMm(2.7d, 100));
        Assert.Equal(
            350d,
            AutoCadFramedG4CompositePolicy.CalculateItemCodeModelHeightMm(3.5d, 100));
    }

    [Fact]
    public void FrameSize_StillComesFromResolveNotHeight()
    {
        var small = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "K1");
        var large = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Rectangle,
            "VT1234");

        Assert.Equal(TimberItemLeaderBlockSize.Small, small.Size);
        Assert.Equal(TimberItemLeaderBlockSize.Large, large.Size);
        Assert.Equal(
            small.Size,
            AutoCadItemLeaderFrameOnlyBlockKey.FromDefinition(small).FrameSize);
        Assert.Equal(
            large.Size,
            AutoCadItemLeaderFrameOnlyBlockKey.FromDefinition(large).FrameSize);
    }
}
