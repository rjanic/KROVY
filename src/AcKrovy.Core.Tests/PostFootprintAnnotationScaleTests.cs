using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class PostFootprintAnnotationScaleTests
{
    private static readonly TimberRectangularFootprintBounds Bounds =
        new(0d, 0d, 200d, 300d);

    [Theory]
    [InlineData(25, 62.5d, 340d)]
    [InlineData(50, 125d, 380d)]
    [InlineData(100, 250d, 460d)]
    public void PostFootprintFullLabel_ScalesTypographyAndGapExactlyOnce(
        int denominator,
        double expectedTextHeightMm,
        double expectedAnchorY)
    {
        var context = Context(denominator);
        var placement = TimberPostFootprintLabelPlacementCalculator.Calculate(
            Bounds,
            TimberPostFootprintLabelPlacementCalculator.VerticalGapMm *
            context.ScaleFactor);

        Assert.Equal(
            expectedTextHeightMm,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor));
        Assert.Equal(100d, placement.AnchorX);
        Assert.Equal(expectedAnchorY, placement.AnchorY);
        Assert.Equal(0d, placement.RotationRadians);
    }

    [Fact]
    public void PostFootprintFullLabel_AtScale50PreservesBasePlacement()
    {
        var baseline = TimberPostFootprintLabelPlacementCalculator.Calculate(Bounds);
        var scaled = TimberPostFootprintLabelPlacementCalculator.Calculate(
            Bounds,
            TimberPostFootprintLabelPlacementCalculator.VerticalGapMm *
            Context(50).ScaleFactor);

        Assert.Equal(baseline, scaled);
    }

    [Theory]
    [InlineData(25, 62.5d, 180d)]
    [InlineData(50, 125d, 360d)]
    [InlineData(100, 250d, 720d)]
    public void PostFootprintDimensionsLeader_ScalesTextAndRunExactlyOnce(
        int denominator,
        double expectedTextHeightMm,
        double expectedFirstSegmentLengthMm)
    {
        var context = Context(denominator);
        var basePlacement = TimberLeaderPlacementCalculator.CalculatePost(Bounds);
        var layout = TimberItemLeaderLayoutCalculator.Calculate(
            basePlacement,
            "80x160\\P2900 mm",
            ItemNumberLeaderStyle.Plain,
            TimberLeaderHorizontalSide.Right,
            context.ScaleFactor);

        Assert.Equal(
            expectedTextHeightMm,
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor));
        Assert.Equal(
            expectedFirstSegmentLengthMm,
            Distance(layout.AnchorX, layout.AnchorY, layout.KneeX, layout.KneeY),
            8);
    }

    [Theory]
    [InlineData(25, 67.5d, 180d)]
    [InlineData(50, 135d, 360d)]
    [InlineData(100, 270d, 720d)]
    public void PostFootprintPlainItemNumber_ScalesTypographyAndLeaderRunExactlyOnce(
        int denominator,
        double expectedTextHeightMm,
        double expectedFirstSegmentLengthMm)
    {
        var context = Context(denominator);
        var basePlacement = TimberLeaderPlacementCalculator.CalculatePost(Bounds);
        var layout = TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
            basePlacement,
            "S1",
            TimberLeaderHorizontalSide.Right,
            context.ScaleFactor);

        Assert.Equal(expectedTextHeightMm, layout.EnvelopeHeightMm);
        Assert.Equal(
            expectedFirstSegmentLengthMm,
            Distance(layout.AnchorX, layout.AnchorY, layout.KneeX, layout.KneeY),
            8);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    public void PostFootprintFramedItem_UsesBaseDefinitionAndSingleBlockScale(
        ItemNumberLeaderStyle style)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, "S1");
        var basePlacement = TimberLeaderPlacementCalculator.CalculatePost(Bounds);
        var at25 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            basePlacement, "S1", style, presentationScaleFactor: 0.5d);
        var at50 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            basePlacement, "S1", style, presentationScaleFactor: 1d);
        var at100 = TimberItemLeaderLayoutCalculator.CalculateBlock(
            basePlacement, "S1", style, presentationScaleFactor: 2d);

        Assert.Equal(definition.WidthMm, at50.EnvelopeWidthMm);
        Assert.Equal(
            TimberItemLeaderBlockDefinitionRules
                .BaseFramedItemTextHeightAtScale50Mm,
            definition.TextHeightMm);
        Assert.Equal(at50.EnvelopeWidthMm * 0.5d, at25.EnvelopeWidthMm);
        Assert.Equal(at50.EnvelopeWidthMm * 2d, at100.EnvelopeWidthMm);
    }

    [Fact]
    public void PostFootprintCombinedPlain_UsesNativeItemAndDimensionTypography()
    {
        var context = Context(100);
        var basePlacement = TimberLeaderPlacementCalculator.CalculatePost(Bounds);
        var itemLayout = TimberItemLeaderLayoutCalculator.CalculatePlainItemNumber(
            basePlacement,
            "S1",
            presentationScaleFactor: context.ScaleFactor);

        Assert.Equal(270d, itemLayout.EnvelopeHeightMm);
        Assert.Equal(
            250d,
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor));
    }

    [Fact]
    public void PostFootprintLayouts_AreDeterministicAcrossCreateAndRefresh()
    {
        var placement = TimberLeaderPlacementCalculator.CalculatePost(Bounds);
        var first = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "S1",
            ItemNumberLeaderStyle.Circle,
            presentationScaleFactor: 2d);
        var refreshed = TimberItemLeaderLayoutCalculator.CalculateBlock(
            placement,
            "S1",
            ItemNumberLeaderStyle.Circle,
            presentationScaleFactor: 2d);

        Assert.Equal(first, refreshed);
        Assert.Equal(first.Side, refreshed.Side);
    }

    [Fact]
    public void ProductionPostFootprintPath_PassesImmutableScaleContext()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementLabelService.cs"));
        var start = source.IndexOf(
            "public static bool UpsertForPostFootprint(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static bool UpsertLabel(",
            start,
            StringComparison.Ordinal);
        var method = source.Substring(start, end - start);

        Assert.Contains(
            "annotationScaleService.Context.ScaleFactor",
            method);
        Assert.Equal(
            1,
            CountOccurrences(
                method,
                "annotationScaleService.Context.ScaleFactor"));
        Assert.DoesNotContain("scaleNativePresentation: false", method);
        Assert.Contains(
            "TimberDimensionTypographyRules.CalculateTextHeightMm",
            method);
    }

    private static TimberAnnotationScaleContext Context(int denominator) =>
        new(denominator, TimberAnnotationScaleSource.Drawing);

    private static double Distance(
        double x1,
        double y1,
        double x2,
        double y2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2d) + Math.Pow(y2 - y1, 2d));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(
                   value,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Repository root was not found.");
    }
}
