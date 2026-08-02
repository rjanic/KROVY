using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFullLabelPresentationPolicyTests
{
    [Theory]
    [InlineData(50, 125d)]
    [InlineData(100, 250d)]
    public void DefaultModelHeights_MatchLegacyTypographyParity(
        int denominator,
        double expectedHeightMm)
    {
        var fromSettings =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                denominator);
        var fromLegacy =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                denominator /
                (double)TimberAnnotationScaleRules.DefaultDenominator);

        Assert.Equal(expectedHeightMm, fromSettings);
        Assert.Equal(expectedHeightMm, fromLegacy);
    }
}
