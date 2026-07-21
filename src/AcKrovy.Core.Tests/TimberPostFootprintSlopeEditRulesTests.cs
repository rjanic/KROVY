using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberPostFootprintSlopeEditRulesTests
{
    [Fact]
    public void FootprintPostDialogDisplaysNinetyDegreesWithoutChangingMetadata()
    {
        var data = FootprintPost() with { SlopeDegrees = 0d };

        var display = TimberPostFootprintSlopeEditRules.ResolveDisplaySlopeDegrees(data, [data]);

        Assert.Equal(90d, display);
        Assert.Equal(0d, data.SlopeDegrees);
        Assert.False(TimberPostFootprintSlopeEditRules.CanEditSlope([data]));
    }

    [Fact]
    public void FootprintPostStillUsesManualLength()
    {
        var data = FootprintPost();

        Assert.Equal(LengthCalculationMode.ManualLength, data.LengthCalculationMode);
        Assert.Equal(2500d, TimberCalculator.CalculateActualLengthMm(data, planLengthMm: null));
    }

    [Fact]
    public void NinetyDegreeDisplayNeverEntersSlopeCorrection()
    {
        var data = FootprintPost() with { SlopeDegrees = 0d };
        var display = TimberPostFootprintSlopeEditRules.ResolveDisplaySlopeDegrees(data, [data]);

        var actual = TimberCalculator.CalculateActualLengthMm(data, planLengthMm: null);

        Assert.Equal(90d, display);
        Assert.Equal(2500d, actual);
        Assert.False(TimberCalculator.IsValidSlopeDegrees(display));
    }

    [Fact]
    public void FootprintPostCuttingLengthUsesManualLengthAndAllowance()
    {
        var data = FootprintPost() with { CuttingAllowanceMm = 200d };

        var measurement = TimberCalculator.Measure(data, planLengthMm: null);

        Assert.Equal(2500d, measurement.ActualLengthMm);
        Assert.Equal(2700d, measurement.CuttingLengthMm);
    }

    [Fact]
    public void LegacyLinePostKeepsStoredSlopeDisplayAndEditingSemantics()
    {
        var legacy = TimberElementDefaults.For(TimberElementType.Post) with
        {
            SlopeDegrees = 35d,
            FootprintWidthEdgeIndex = null,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500d,
        };

        Assert.False(TimberPostFootprintSlopeEditRules.UsesPerpendicularPresentation([legacy]));
        Assert.True(TimberPostFootprintSlopeEditRules.CanEditSlope([legacy]));
        Assert.Equal(
            35d,
            TimberPostFootprintSlopeEditRules.ResolveDisplaySlopeDegrees(legacy, [legacy]));
    }

    [Fact]
    public void MixedFootprintAndLegacySelectionDoesNotForceNinetyDegreePresentation()
    {
        var footprint = FootprintPost();
        var legacy = TimberElementDefaults.For(TimberElementType.Post) with
        {
            FootprintWidthEdgeIndex = null,
        };

        Assert.False(TimberPostFootprintSlopeEditRules.UsesPerpendicularPresentation(
            [footprint, legacy]));
    }

    [Fact]
    public void FootprintPostDisablesSlopeDirectionEditor()
    {
        var footprint = FootprintPost();

        var canEditDirection = TimberPostFootprintSlopeEditRules.CanEditSlopeDirection([footprint]);

        Assert.False(canEditDirection);
    }

    [Fact]
    public void FootprintPostDoesNotCreateSlopeDirectionPatch()
    {
        var footprint = FootprintPost() with { IsSlopeDirectionReversed = false };

        var patch = TimberPostFootprintSlopeEditRules.ResolveSlopeDirectionPatch(
            [footprint],
            shouldApply: true,
            isReversed: true);

        Assert.Null(patch);
        Assert.False(footprint.IsSlopeDirectionReversed);
    }

    [Fact]
    public void LegacyLinePostKeepsSlopeDirectionEditable()
    {
        var legacy = TimberElementDefaults.For(TimberElementType.Post) with
        {
            FootprintWidthEdgeIndex = null,
        };

        Assert.True(TimberPostFootprintSlopeEditRules.CanEditSlopeDirection([legacy]));
        Assert.True(TimberPostFootprintSlopeEditRules.ResolveSlopeDirectionPatch(
            [legacy],
            shouldApply: true,
            isReversed: true));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter)]
    [InlineData(TimberElementType.WallPlate)]
    [InlineData(TimberElementType.Purlin)]
    [InlineData(TimberElementType.CollarTie)]
    [InlineData(TimberElementType.Brace)]
    [InlineData(TimberElementType.TieBeam)]
    public void OtherElementTypesKeepSlopeDirectionEditable(TimberElementType elementType)
    {
        var data = TimberElementDefaults.For(elementType);

        Assert.True(TimberPostFootprintSlopeEditRules.CanEditSlopeDirection([data]));
        Assert.False(TimberPostFootprintSlopeEditRules.ResolveSlopeDirectionPatch(
            [data],
            shouldApply: true,
            isReversed: false));
    }

    private static TimberElementData FootprintPost() =>
        TimberPostFootprintAssignmentRules.CreateMetadata(
            TimberElementDefaults.For(TimberElementType.Post) with
            {
                SlopeDegrees = 0d,
                ManualLengthMm = null,
            },
            new TimberRectangularFootprintDimensions(
                WidthMm: 140d,
                HeightMm: 140d,
                WidthEdgeIndex: 0,
                HeightEdgeIndex: 1));
}
