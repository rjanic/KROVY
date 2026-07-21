using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class TimberPostFootprintRuntimeReadingTests
{
    [Fact]
    public void FootprintMeasurement_HasNoPlanLengthAndUsesManualAndCuttingLengths()
    {
        var snapshot = CreateFootprintSnapshot(widthEdgeIndex: 0);

        var measurement = TimberElementMeasurer.Measure(snapshot);

        Assert.Null(measurement.PlanLengthMm);
        Assert.Equal(2500, measurement.ActualLengthMm);
        Assert.Equal(2600, measurement.CuttingLengthMm);
        Assert.Equal(0.145145, measurement.VolumeM3, precision: 6);
    }

    [Fact]
    public void FootprintDimensions_AreResolvedFromCurrentGeometryByStoredWidthEdge()
    {
        var geometry = Rectangle(widthMm: 319, heightMm: 175);

        var normal = TimberRectangularFootprintEdgeRules.ResolveDimensions(geometry, widthEdgeIndex: 0);
        var reversed = TimberRectangularFootprintEdgeRules.ResolveDimensions(geometry, widthEdgeIndex: 1);

        Assert.Equal((319d, 175d), (normal.WidthMm, normal.HeightMm));
        Assert.Equal((175d, 319d), (reversed.WidthMm, reversed.HeightMm));
    }

    [Fact]
    public void FootprintReport_UsesGeometryDimensionsAndCuttingLengthWithoutPerimeter()
    {
        var measurement = TimberElementMeasurer.Measure(CreateFootprintSnapshot(widthEdgeIndex: 0));

        var report = TimberReportBuilder.Build(new[] { measurement });

        var line = Assert.Single(report.Lines);
        Assert.Equal(319, line.WidthMm);
        Assert.Equal(175, line.HeightMm);
        Assert.Equal(2600, line.CuttingLengthMm);
        Assert.Equal(2600, line.TotalLengthMm);
        Assert.NotEqual(988, line.CuttingLengthMm);
    }

    [Fact]
    public void LegacyLinePost_KeepsItsAvailablePlanLength()
    {
        var data = TimberElementDefaults.For(TimberElementType.Post) with
        {
            WidthMm = 140,
            HeightMm = 140,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
            FootprintWidthEdgeIndex = null,
        };

        var measurement = TimberElementMeasurer.Measure(new TimberElementSnapshot(data, 900));

        Assert.Equal(900, measurement.PlanLengthMm);
        Assert.Equal(2500, measurement.ActualLengthMm);
    }

    [Theory]
    [InlineData("sk-SK", "Pôdorysná dĺžka")]
    [InlineData("cs-CZ", "Půdorysná délka")]
    [InlineData("en-US", "Plan length")]
    [InlineData("de-DE", "Grundrisslänge")]
    [InlineData("pl-PL", "Długość w rzucie")]
    [InlineData("fr-FR", "Longueur en projection horizontale")]
    public void FootprintInspectSummary_DoesNotContainPlanLength(
        string cultureName,
        string localizedPlanLength)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        var summary = string.Format(
            culture,
            UiStrings.GetString("Command_Inspect_FootprintSummaryFormat", culture),
            "S1", "Post", 319d, 175d, 2.5d, 2.6d, 0.1456d);

        Assert.DoesNotContain(localizedPlanLength, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("988", summary, StringComparison.Ordinal);
    }

    private static TimberElementSnapshot CreateFootprintSnapshot(int widthEdgeIndex)
    {
        var geometry = Rectangle(widthMm: 319, heightMm: 175);
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(geometry, widthEdgeIndex);
        var data = TimberElementDefaults.For(TimberElementType.Post) with
        {
            ElementId = "S1",
            WidthMm = dimensions.WidthMm,
            HeightMm = dimensions.HeightMm,
            CuttingAllowanceMm = 100,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
            FootprintWidthEdgeIndex = widthEdgeIndex,
        };

        return new TimberElementSnapshot(data, PlanLengthMm: null);
    }

    private static TimberRectangularFootprintGeometry Rectangle(double widthMm, double heightMm) =>
        TimberRectangularFootprintValidator.Validate(
        [
            new(0, 0),
            new(widthMm, 0),
            new(widthMm, heightMm),
            new(0, heightMm),
        ]).Geometry!;
}
