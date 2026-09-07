using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterSpacingRulesTests
{
    [Fact]
    public void MissingSettingsResolveTo900DefaultAnd500Minimum()
    {
        var settings = RoofRafterSpacingRules.Resolve(false, null);

        Assert.Equal(900d, settings.DefaultAutomaticSpacingMm);
        Assert.Equal(500d, settings.MinimumAutomaticSpacingMm);
    }

    [Theory]
    [InlineData(900d, 500d, true)]
    [InlineData(400d, 500d, false)]
    [InlineData(700d, 650d, true)]
    [InlineData(double.NaN, 500d, false)]
    [InlineData(900d, double.PositiveInfinity, false)]
    [InlineData(900d, 0d, false)]
    [InlineData(900d, -1d, false)]
    public void SettingsRequirePositiveFiniteValuesAndDefaultAtLeastMinimum(
        double defaultSpacing,
        double minimumSpacing,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofRafterSpacingRules.IsValidSettings(
                defaultSpacing,
                minimumSpacing));
    }

    [Theory]
    [InlineData(499d, 500d, false)]
    [InlineData(500d, 500d, true)]
    [InlineData(600d, 500d, true)]
    [InlineData(900d, 500d, true)]
    [InlineData(449d, 450d, false)]
    [InlineData(450d, 450d, true)]
    [InlineData(600d, 650d, false)]
    [InlineData(650d, 650d, true)]
    public void AutomaticWorkingSpacingUsesConfiguredMinimum(
        double workingSpacing,
        double configuredMinimum,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofRafterSpacingRules.IsValidAutomaticWorkingSpacing(
                workingSpacing,
                configuredMinimum));
    }

    [Fact]
    public void ValidStoredSettingsAreReturnedWithoutClamping()
    {
        var stored = new RoofRafterSettings(775d, 450d);

        Assert.Same(stored, RoofRafterSpacingRules.Resolve(true, stored));
    }
}
