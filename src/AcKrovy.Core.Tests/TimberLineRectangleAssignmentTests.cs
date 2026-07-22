using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberLineRectangleAssignmentTests
{
    [Fact]
    public void SelectedShortLine_ProducesSchemaV2PostWithShortByLongDimensions()
    {
        var metadata = CreateMetadata("A");

        Assert.Equal(TimberElementDataSchema.CurrentVersion, metadata.SchemaVersion);
        Assert.Equal(TimberElementType.Post, metadata.ElementType);
        Assert.Equal(140d, metadata.WidthMm);
        Assert.Equal(200d, metadata.HeightMm);
        Assert.Equal(0, metadata.FootprintWidthEdgeIndex);
        Assert.Equal(LengthCalculationMode.ManualLength, metadata.LengthCalculationMode);
        Assert.Equal(TimberPostFootprintAssignmentRules.DefaultManualLengthMm, metadata.ManualLengthMm);
    }

    [Fact]
    public void SelectedLongLine_ProducesLongByShortDimensions()
    {
        var metadata = CreateMetadata("B");

        Assert.Equal(200d, metadata.WidthMm);
        Assert.Equal(140d, metadata.HeightMm);
        Assert.Equal(0, metadata.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void ConvertedPostActualLength_DoesNotUseSourceRectanglePerimeter()
    {
        var metadata = CreateMetadata("A") with { CuttingAllowanceMm = 200d };
        var perimeter = 2d * (140d + 200d);

        var measurement = TimberElementMeasurer.Measure(
            new TimberElementSnapshot(metadata, PlanLengthMm: null));

        Assert.Equal(2500d, measurement.ActualLengthMm);
        Assert.NotEqual(perimeter, measurement.ActualLengthMm);
        Assert.Equal(2700d, measurement.CuttingLengthMm);
    }

    [Fact]
    public void ConversionInputIdentities_AreNotAddedToTechnicalMetadata()
    {
        var discovery = Discover("A");
        var plan = TimberLineRectangleConversionPlan.FromDiscovery(discovery);
        var metadata = CreateMetadata("A");

        Assert.Equal(["A", "B", "C", "D"], plan.SourceEdgeKeys);
        Assert.DoesNotContain(
            typeof(TimberElementData).GetProperties(),
            property => property.Name.Contains("Source", StringComparison.Ordinal));
        Assert.Equal(TimberElementType.Post, metadata.ElementType);
    }

    private static TimberElementData CreateMetadata(string selectedKey)
    {
        var discovery = Discover(selectedKey);
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            discovery.Geometry!,
            TimberLineRectangleDiscoveryResult.SelectedWidthEdgeIndex);
        return TimberPostFootprintAssignmentRules.CreateMetadata(
            TimberElementDefaults.For(TimberElementType.Post) with { ManualLengthMm = null },
            dimensions);
    }

    private static TimberLineRectangleDiscoveryResult Discover(string selectedKey) =>
        TimberLineRectangleDiscoveryService.Discover(
            selectedKey,
            [
                new("A", new(0, 0), new(140, 0)),
                new("B", new(140, 0), new(140, 200)),
                new("C", new(140, 200), new(0, 200)),
                new("D", new(0, 200), new(0, 0)),
            ]);
}
