using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadPlainItemLeaderPresentationPolicyTests
{
    [Theory]
    [InlineData(50, 135d)]
    [InlineData(100, 270d)]
    public void DefaultItemNumberModelHeights_MatchLegacyTypographyParity(
        int denominator,
        double expectedHeightMm)
    {
        var fromSettings =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                denominator);
        var fromLegacy =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
    }

    [Theory]
    [InlineData(3d, 50, 150d)]
    [InlineData(2.7d, 75, 202.5d)]
    public void ExplicitItemNumberPaperHeight_ScalesByDenominator(
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
