using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadDimensionsLeaderPresentationPolicyTests
{
    [Theory]
    [InlineData(50, 125d)]
    [InlineData(100, 250d)]
    public void DefaultLabelAndDimensionModelHeights_MatchLegacyTypographyParity(
        int denominator,
        double expectedHeightMm)
    {
        var fromSettings =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                denominator);
        var fromLegacy =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm *
            denominator);
    }

    [Theory]
    [InlineData(3d, 50, 150d)]
    [InlineData(2.5d, 100, 250d)]
    [InlineData(2.7d, 75, 202.5d)]
    public void ExplicitLabelAndDimensionPaperHeight_ScalesByDenominator(
        double paperHeightMm,
        int denominator,
        double expectedHeightMm)
    {
        Assert.Equal(
            expectedHeightMm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                denominator));
    }
}
