using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationScaleResolverTests
{
    [Fact]
    public void ScaleSource_ElementOverrideDoesNotRenumberExistingValues()
    {
        Assert.Equal(0, (int)TimberAnnotationScaleSource.Drawing);
        Assert.Equal(1, (int)TimberAnnotationScaleSource.UserDefault);
        Assert.Equal(2, (int)TimberAnnotationScaleSource.FixedDefault);
        Assert.Equal(3, (int)TimberAnnotationScaleSource.ElementOverride);
    }

    [Theory]
    [InlineData(5, 50)]
    [InlineData(250, 25)]
    public void ResolveElementContext_ValidOverrideWins(
        int elementOverride,
        int drawingDenominator)
    {
        var drawing = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: true,
            drawingDenominator);

        var result = TimberAnnotationScaleResolver.ResolveElementContext(
            drawing,
            elementOverride);

        Assert.Equal(elementOverride, result.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.ElementOverride, result.Source);
    }

    [Theory]
    [InlineData(null, 75)]
    [InlineData(4, 25)]
    [InlineData(251, 100)]
    public void ResolveElementContext_MissingOrInvalidOverrideKeepsDrawingContext(
        int? elementOverride,
        int drawingDenominator)
    {
        var drawing = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: true,
            drawingDenominator);

        var result = TimberAnnotationScaleResolver.ResolveElementContext(
            drawing,
            elementOverride);

        Assert.Same(drawing, result);
        Assert.Equal(drawingDenominator, result.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.Drawing, result.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    [InlineData(251)]
    public void ResolveElementContext_WithoutDrawingUsesFixedDefault(
        int? elementOverride)
    {
        var drawing = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: false,
            drawingDenominator: 25);

        var result = TimberAnnotationScaleResolver.ResolveElementContext(
            drawing,
            elementOverride);

        Assert.Same(drawing, result);
        Assert.Equal(50, result.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.FixedDefault, result.Source);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(251)]
    public void ResolveDrawingContext_InvalidDrawingUsesFixedDefaultSource(
        int drawingDenominator)
    {
        var result = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: true,
            drawingDenominator);

        Assert.Equal(50, result.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.FixedDefault, result.Source);
    }

    [Fact]
    public void ResolveMixedBatch_UsesPerElementContextsAndMetrics()
    {
        var drawing = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: true,
            drawingDenominator: 50);
        int?[] overrides = [null, 25, 100];

        var contexts = overrides
            .Select(value => TimberAnnotationScaleResolver.ResolveElementContext(
                drawing,
                value))
            .ToArray();
        var textHeights = contexts
            .Select(context => TimberDimensionTypographyRules.CalculateTextHeightMm(
                context.ScaleFactor))
            .ToArray();

        Assert.Equal([50, 25, 100], contexts.Select(context => context.Denominator));
        Assert.Equal(
            [
                TimberAnnotationScaleSource.Drawing,
                TimberAnnotationScaleSource.ElementOverride,
                TimberAnnotationScaleSource.ElementOverride,
            ],
            contexts.Select(context => context.Source));
        Assert.Equal(
            [1d, 0.5d, 2d],
            contexts.Select(context => context.ScaleFactor));
        Assert.Equal(3, textHeights.Distinct().Count());
    }
}
