using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SlopeAnnotationScaleTests
{
    [Theory]
    [InlineData(25, 40d)]
    [InlineData(50, 80d)]
    [InlineData(100, 160d)]
    [InlineData(0, 80d)]
    public void SlopeTextTypography_UsesCentralScaleRule(
        int denominator,
        double expectedTextHeightMm)
    {
        var context = Context(denominator);

        Assert.Equal(
            expectedTextHeightMm,
            TimberSlopeAnnotationPresentationRules.CalculateTextHeightMm(
                context.ScaleFactor));
    }

    [Theory]
    [InlineData(25, 50d)]
    [InlineData(50, 100d)]
    [InlineData(100, 200d)]
    [InlineData(0, 100d)]
    public void SlopeTextOffset_UsesCalibratedCentralScaleRule(
        int denominator,
        double expectedTextOffsetMm)
    {
        Assert.Equal(
            expectedTextOffsetMm,
            TimberSlopeAnnotationPresentationRules.CalculateTextOffsetMm(
                Context(denominator).ScaleFactor));
    }

    [Theory]
    [InlineData(25, 1d / 3d)]
    [InlineData(50, 2d / 3d)]
    [InlineData(100, 4d / 3d)]
    [InlineData(0, 2d / 3d)]
    public void ZeroAndNinetyDegreeSymbols_UseSingleEffectiveBlockScale(
        int denominator,
        double expectedScale)
    {
        var effectiveScale =
            TimberSlopeAnnotationPresentationRules.CalculateSpecialSymbolScale(
                Context(denominator).ScaleFactor);

        Assert.Equal(expectedScale, effectiveScale, 10);
    }

    [Theory]
    [InlineData(25, 20d, 33.333333333333336d, 53.333333333333336d)]
    [InlineData(50, 40d, 66.66666666666667d, 106.66666666666667d)]
    [InlineData(100, 80d, 133.33333333333334d, 213.33333333333334d)]
    public void NinetyDegreeSymbolLocalGeometryAndTextOffsetScaleTogether(
        int denominator,
        double expectedCapHalfLengthMm,
        double expectedStemLengthMm,
        double expectedTextLongitudinalOffsetMm)
    {
        var symbolScale =
            TimberSlopeAnnotationPresentationRules.CalculateSpecialSymbolScale(
                Context(denominator).ScaleFactor);
        var geometry =
            TimberPostAnnotationGeometryCalculator.CreateLocal(symbolScale);

        Assert.Equal(-expectedCapHalfLengthMm, geometry.CapStart.X, 10);
        Assert.Equal(expectedCapHalfLengthMm, geometry.CapEnd.X, 10);
        Assert.Equal(expectedStemLengthMm, geometry.StemEnd.Y, 10);
        Assert.Equal(
            expectedTextLongitudinalOffsetMm,
            geometry.TextPosition.X,
            10);
    }

    [Theory]
    [InlineData(25, 90d, 65d, 28d, 50d, 50d)]
    [InlineData(50, 180d, 130d, 56d, 100d, 100d)]
    [InlineData(100, 360d, 260d, 112d, 200d, 200d)]
    public void SlopeArrowAndPlacementDimensions_ScaleExactlyOnce(
        int denominator,
        double expectedAxisLengthMm,
        double expectedHeadSpanLongitudinalMm,
        double expectedHeadWidthMm,
        double expectedTextOffsetMm,
        double expectedClearanceMm)
    {
        var context = Context(denominator);
        var placement = TimberSlopeArrowCalculator.Calculate(
            0d,
            0d,
            2000d,
            0d,
            1000d,
            0d,
            isReversed: false,
            context.ScaleFactor);

        Assert.Equal(
            expectedAxisLengthMm,
            placement.TipX - placement.TailX,
            8);
        Assert.Equal(
            expectedHeadSpanLongitudinalMm,
            2d * (placement.TipX - placement.HeadLeftX),
            8);
        Assert.Equal(
            expectedHeadWidthMm,
            placement.HeadLeftY - placement.HeadRightY,
            8);
        Assert.Equal(
            expectedTextOffsetMm,
            TimberSlopeAnnotationPresentationRules.CalculateTextOffsetMm(
                context.ScaleFactor));
        Assert.Equal(
            expectedClearanceMm,
            TimberSlopeAnnotationPresentationRules.ScaleLength(
                TimberSlopeAnnotationPlacementCalculator
                    .SlopeAnnotationLabelClearanceMm,
                context.ScaleFactor));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public void ScaleDoesNotChangeNormalOrFlippedDirection(int denominator)
    {
        var factor = Context(denominator).ScaleFactor;
        var normal = TimberSlopeArrowCalculator.Calculate(
            0d, 0d, 2000d, 0d, 1000d, 0d, false, factor);
        var flipped = TimberSlopeArrowCalculator.Calculate(
            0d, 0d, 2000d, 0d, 1000d, 0d, true, factor);

        Assert.True(normal.TipX > normal.TailX);
        Assert.True(flipped.TipX < flipped.TailX);
        Assert.Equal(normal.TipX, flipped.TailX, 8);
        Assert.Equal(normal.TailX, flipped.TipX, 8);
        Assert.Equal(1000d, (normal.TipX + normal.TailX) / 2d, 8);
        Assert.Equal(1000d, (flipped.TipX + flipped.TailX) / 2d, 8);
    }

    [Fact]
    public void RepeatedSlopeRefreshCalculation_IsDeterministic()
    {
        var first = TimberSlopeArrowCalculator.Calculate(
            0d, 0d, 3000d, 0d, 1000d, 0d, false, 2d);
        var refreshed = TimberSlopeArrowCalculator.Calculate(
            0d, 0d, 3000d, 0d, 1000d, 0d, false, 2d);

        Assert.Equal(first, refreshed);
    }

    [Fact]
    public void SlopeLayerDefault_IsAci40()
    {
        Assert.Equal(
            40,
            TimberSlopeAnnotationPresentationRules.DefaultLayerColorIndex);
    }

    [Fact]
    public void ProductionSlopePipeline_UsesImmutableScaleContextAndByLayer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var annotations = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "SlopeAnnotationService.cs"));
        var arrows = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "SlopeArrowService.cs"));
        var text = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "SlopeAngleTextService.cs"));

        Assert.Contains(
            "annotationScaleContext.ScaleFactor",
            annotations);
        Assert.Contains("presentationScaleFactor", arrows);
        Assert.Contains("presentationScaleFactor", text);
        Assert.Contains(
            "TimberSlopeAnnotationPresentationRules.DefaultLayerColorIndex",
            arrows);
        Assert.Contains(
            "TimberSlopeAnnotationPresentationRules.DefaultLayerColorIndex",
            text);
        Assert.Contains("arrow.LineWeight = LineWeight.ByLayer", arrows);
        Assert.Contains("angleText.LineWeight = LineWeight.ByLayer", text);
        Assert.Contains("updateExistingLayer: false", arrows);
        Assert.Contains("updateExistingLayer: false", text);
        Assert.DoesNotContain("EnableAnnotationScale = true", arrows);
        Assert.DoesNotContain("EnableAnnotationScale = true", text);
    }

    [Fact]
    public void SpecialSlopeSymbols_UseOnlyBlockReferenceScale()
    {
        var arrows = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "SlopeArrowService.cs"));

        Assert.Contains(
            "CalculateSpecialSymbolScale(presentationScaleFactor)",
            arrows);
        Assert.Contains("marker.ScaleFactors = new Scale3d(", arrows);
        Assert.Contains(
            "AddHorizontalMarkerLine(database, transaction, definition, -HorizontalMarkerHalfGapMm)",
            arrows);
        Assert.Contains(
            "var symbol = TimberPostAnnotationGeometryCalculator.CreateLocal();",
            arrows);
        Assert.DoesNotContain("ScaleBy(", arrows);
    }

    [Fact]
    public void FootprintNinetyDegreeBlock_CreateAndUpdateApplyEntireReferenceScale()
    {
        var service = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "PostFootprintPerpendicularAnnotationService.cs"));
        var start = service.IndexOf(
            "public static bool UpsertForFootprint(",
            StringComparison.Ordinal);
        var end = service.IndexOf(
            "public static int DeleteForSourceHandle(",
            start,
            StringComparison.Ordinal);
        var upsert = service.Substring(start, end - start);

        Assert.Contains(
            "BlockName = \"DECORAIR_ACADKROVY_POST_FOOTPRINT_90_V2\"",
            service);
        Assert.Contains(
            "annotation = new BlockReference(Point3d.Origin, blockId)",
            upsert);
        Assert.Contains(
            "annotation = (BlockReference)transaction.GetObject(selected!.Id, OpenMode.ForWrite)",
            upsert);
        Assert.Contains(
            "annotation.ScaleFactors = new Scale3d(",
            upsert);
        Assert.Contains(
            "CalculateSpecialSymbolScale(",
            upsert);
        Assert.Contains(
            "annotationScaleContext.ScaleFactor",
            upsert);
        Assert.DoesNotContain("new Scale3d(1d)", upsert);
        Assert.DoesNotContain("ScaleBy(", service);
        Assert.Contains(
            "var local = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();",
            service);
    }

    [Fact]
    public void FlipSlopePassesCurrentBatchScaleContextToSlopeRefresh()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs"));
        var start = source.IndexOf(
            "public void FlipSlopeDirection()",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "[CommandMethod(AcKrovyCommandNames.Inspect",
            start,
            StringComparison.Ordinal);
        var method = source.Substring(start, end - start);

        Assert.Equal(
            1,
            CountOccurrences(
                method,
                "AutoCadAnnotationPresentationBatchContext.Create("));
        Assert.Contains(
            "SlopeAnnotationService.EnsureForElement(",
            method);
        Assert.Contains("presentationBatchContext.ResolveForElement(updated)", method);
        Assert.Contains(".AnnotationScaleContext", method);
    }

    private static TimberAnnotationScaleContext Context(int denominator) =>
        new(denominator, TimberAnnotationScaleSource.Drawing);

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
