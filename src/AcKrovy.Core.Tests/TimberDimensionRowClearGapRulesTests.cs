using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberDimensionRowClearGapRulesTests
{
    public const double DimensionPaperHeightMm = 2.5d;

    [Theory]
    [InlineData(25, 62.5d, 50d, 112.5d, 56.25d)]
    [InlineData(50, 125d, 100d, 225d, 112.5d)]
    [InlineData(100, 250d, 200d, 450d, 225d)]
    public void ClearGapContract_UsesTextHeightPlusPaperGap(
        int denominator,
        double expectedTextHeight,
        double expectedClearGap,
        double expectedCenterDistance,
        double expectedHalfOffset)
    {
        Assert.Equal(2.0d, TimberDimensionRowClearGapRules.DesiredClearGapPaperMm);

        var textHeight =
            TimberDimensionRowClearGapRules.CalculateDimensionTextModelHeightMm(
                DimensionPaperHeightMm,
                denominator);
        var clearGap =
            TimberDimensionRowClearGapRules.CalculateDesiredClearGapModelMm(denominator);
        var center =
            TimberDimensionRowClearGapRules.CalculateRowCenterDistanceModelMm(
                DimensionPaperHeightMm,
                denominator);
        var half =
            TimberDimensionRowClearGapRules.CalculateHalfRowCenterDistanceModelMm(
                DimensionPaperHeightMm,
                denominator);
        var widthY =
            TimberDimensionRowClearGapRules.CalculateWidthLocalY(
                DimensionPaperHeightMm,
                denominator);
        var heightY =
            TimberDimensionRowClearGapRules.CalculateHeightLocalY(
                DimensionPaperHeightMm,
                denominator);

        Assert.Equal(expectedTextHeight, textHeight);
        Assert.Equal(expectedClearGap, clearGap);
        Assert.Equal(expectedCenterDistance, center);
        Assert.Equal(expectedHalfOffset, half);
        Assert.Equal(expectedHalfOffset, widthY);
        Assert.Equal(-expectedHalfOffset, heightY);
        Assert.Equal(0d, (widthY + heightY) / 2d);
        Assert.Equal(
            expectedClearGap,
            TimberDimensionRowClearGapRules.CalculateActualGlyphClearGapModelMm(
                center,
                textHeight));
    }

    [Theory]
    [InlineData(2.7d, 25, 67.5d)]
    [InlineData(2.7d, 50, 135d)]
    [InlineData(2.7d, 100, 270d)]
    [InlineData(3.0d, 25, 75d)]
    [InlineData(3.0d, 50, 150d)]
    [InlineData(3.0d, 100, 300d)]
    public void ItemModelHeight_ScalesPaperTimesDenominator(
        double itemPaperHeightMm,
        int denominator,
        double expectedModelHeight)
    {
        Assert.Equal(2.7d, TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm);

        var model = TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            itemPaperHeightMm,
            denominator);
        Assert.Equal(expectedModelHeight, model);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(251)]
    public void InvalidDenominator_Throws(int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberDimensionRowClearGapRules.CalculateDesiredClearGapModelMm(denominator));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberDimensionRowClearGapRules.CalculateDimensionTextModelHeightMm(
                DimensionPaperHeightMm,
                denominator));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPaperHeight_Throws(double paperHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberDimensionRowClearGapRules.CalculateDimensionTextModelHeightMm(
                paperHeight,
                50));
    }
}
