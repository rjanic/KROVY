using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AnnotationScaleSettingsTests
{
    [Theory]
    [InlineData(25, TimberAnnotationScalePreset.Scale25)]
    [InlineData(50, TimberAnnotationScalePreset.Scale50)]
    [InlineData(75, TimberAnnotationScalePreset.Scale75)]
    [InlineData(100, TimberAnnotationScalePreset.Scale100)]
    [InlineData(5, TimberAnnotationScalePreset.Custom)]
    [InlineData(250, TimberAnnotationScalePreset.Custom)]
    public void PresetMappingKeepsFourFixedOptionsAndSupportsCustomRange(
        int denominator,
        TimberAnnotationScalePreset expected) =>
        Assert.Equal(expected, TimberAnnotationScaleSettingsRules.GetPreset(denominator));

    [Theory]
    [InlineData(25, 62.5, 67.5, 40, 0.5)]
    [InlineData(50, 125, 135, 80, 1)]
    [InlineData(75, 187.5, 202.5, 120, 1.5)]
    [InlineData(100, 250, 270, 160, 2)]
    public void PreviewUsesProductionTypographyRules(
        int denominator,
        double dimensionHeight,
        double itemHeight,
        double slopeHeight,
        double blockScale)
    {
        var preview = TimberAnnotationScaleSettingsRules.CreatePreview(denominator);

        Assert.Equal(dimensionHeight, preview.DimensionTextHeightMm);
        Assert.Equal(2.5d, preview.DimensionPaperTextHeightMm);
        Assert.Equal(itemHeight, preview.ItemNumberTextHeightMm);
        Assert.Equal(2.7d, preview.ItemNumberPaperTextHeightMm);
        Assert.Equal(slopeHeight, preview.SlopeTextHeightMm);
        Assert.Equal(1.6d, preview.SlopePaperTextHeightMm);
        Assert.Equal(blockScale, preview.FramedBlockScale);
    }

    [Fact]
    public void CustomPreviewUsesGeneralModelToPaperCalculation()
    {
        var preview = TimberAnnotationScaleSettingsRules.CreatePreview(60);

        Assert.Equal(150d, preview.DimensionTextHeightMm);
        Assert.Equal(2.5d, preview.DimensionPaperTextHeightMm);
        Assert.Equal(162d, preview.ItemNumberTextHeightMm);
        Assert.Equal(2.7d, preview.ItemNumberPaperTextHeightMm);
        Assert.Equal(96d, preview.SlopeTextHeightMm);
        Assert.Equal(1.6d, preview.SlopePaperTextHeightMm);
        Assert.Equal(1.2d, preview.FramedBlockScale);
    }
}
