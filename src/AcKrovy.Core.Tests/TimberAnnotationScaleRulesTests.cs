using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationScaleRulesTests
{
    [Fact]
    public void Constants_DefineTheLockedAnnotationScaleRange()
    {
        Assert.Equal(50, TimberAnnotationScaleRules.DefaultDenominator);
        Assert.Equal(5, TimberAnnotationScaleRules.MinimumDenominator);
        Assert.Equal(250, TimberAnnotationScaleRules.MaximumDenominator);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(250, true)]
    [InlineData(4, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(251, false)]
    public void IsValidDenominator_UsesTheInclusiveLockedRange(
        int denominator,
        bool expected) =>
        Assert.Equal(expected, TimberAnnotationScaleRules.IsValidDenominator(denominator));

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(250)]
    public void NormalizeDenominator_KeepsValidValues(int denominator) =>
        Assert.Equal(denominator, TimberAnnotationScaleRules.NormalizeDenominator(denominator));

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(251)]
    public void NormalizeDenominator_UsesDefaultInsteadOfClamping(int denominator) =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleRules.NormalizeDenominator(denominator));

    [Theory]
    [InlineData(25, 0.5d)]
    [InlineData(50, 1.0d)]
    [InlineData(75, 1.5d)]
    [InlineData(100, 2.0d)]
    [InlineData(250, 5.0d)]
    public void GetScaleFactor_UsesTheOneToFiftyBaseScale(
        int denominator,
        double expected) =>
        Assert.Equal(expected, TimberAnnotationScaleRules.GetScaleFactor(denominator));

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(251)]
    public void GetScaleFactor_InvalidDenominatorUsesDefaultFactor(int denominator) =>
        Assert.Equal(1.0d, TimberAnnotationScaleRules.GetScaleFactor(denominator));

    [Theory]
    [InlineData(180d, 25, 90d)]
    [InlineData(180d, 50, 180d)]
    [InlineData(180d, 100, 360d)]
    [InlineData(120d, 100, 240d)]
    public void ScaleLength_AppliesTheScaleFactorExactlyOnce(
        double baseLengthMm,
        int denominator,
        double expected) =>
        Assert.Equal(
            expected,
            TimberAnnotationScaleRules.ScaleLength(baseLengthMm, denominator));

    [Fact]
    public void ScaleLength_ZeroBaseLengthRemainsZero() =>
        Assert.Equal(0d, TimberAnnotationScaleRules.ScaleLength(0d, 100));
}
