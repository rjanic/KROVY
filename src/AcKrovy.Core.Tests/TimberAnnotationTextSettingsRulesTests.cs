using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationTextSettingsRulesTests
{
    [Fact]
    public void Default_PreservesLegacyPaperHeightsAndUsesStableStyleName()
    {
        var settings = TimberAnnotationTextSettingsRules.Default;

        Assert.Equal("Standard", settings.TextStyleName);
        Assert.Equal(2.5d, settings.LabelAndDimensionPaperHeightMm);
        Assert.Equal(2.7d, settings.ItemNumberPaperHeightMm);
        Assert.Equal(1.6d, settings.SlopeAnglePaperHeightMm);
        Assert.True(TimberAnnotationTextSettingsRules.IsValid(settings));
    }

    [Fact]
    public void Settings_RecordUsesValueEqualityAndWithDoesNotMutateSource()
    {
        var source = Settings("ISOCP");
        var equal = Settings("ISOCP");

        var changed = source with { ItemNumberPaperHeightMm = 3.1d };

        Assert.Equal(source, equal);
        Assert.NotSame(source, equal);
        Assert.Equal(2.7d, source.ItemNumberPaperHeightMm);
        Assert.Equal(3.1d, changed.ItemNumberPaperHeightMm);
        Assert.NotEqual(source, changed);
    }

    [Fact]
    public void LegacyTypographyDefaultsDeriveFromCentralPaperDefaults()
    {
        Assert.Equal(
            TimberDimensionTypographyRules.BaseDimensionTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
        Assert.Equal(
            TimberItemNumberTypographyRules.BaseItemNumberTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
        Assert.Equal(
            TimberSlopeAnnotationPresentationRules.BaseTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultSlopeAnglePaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(10d)]
    public void LabelAndDimensionHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules
                .IsValidLabelAndDimensionPaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(10.001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void LabelAndDimensionHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules
                .IsValidLabelAndDimensionPaperHeightMm(value));

    [Theory]
    [InlineData(1d)]
    [InlineData(3.5d)]
    public void ItemNumberHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules
                .IsValidItemNumberPaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(3.501d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ItemNumberHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules
                .IsValidItemNumberPaperHeightMm(value));

    [Theory]
    [InlineData(1d)]
    [InlineData(5d)]
    public void SlopeAngleHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules
                .IsValidSlopeAnglePaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(5.001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SlopeAngleHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules
                .IsValidSlopeAnglePaperHeightMm(value));

    [Fact]
    public void ValidateAndNormalize_TrimsStyleNameWithoutChangingHeights()
    {
        var source = Settings("  ISOCP  ");

        var normalized =
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(source);

        Assert.Equal("ISOCP", normalized.TextStyleName);
        Assert.Equal(source.LabelAndDimensionPaperHeightMm,
            normalized.LabelAndDimensionPaperHeightMm);
        Assert.Equal(source.ItemNumberPaperHeightMm,
            normalized.ItemNumberPaperHeightMm);
        Assert.Equal(source.SlopeAnglePaperHeightMm,
            normalized.SlopeAnglePaperHeightMm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Style\nName")]
    public void ValidateAndNormalize_RejectsInvalidStyleNames(string name) =>
        Assert.Throws<ArgumentException>(() =>
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(
                Settings(name)));

    [Fact]
    public void TextStyleName_Accepts255AndRejects256Characters()
    {
        Assert.True(TimberAnnotationTextSettingsRules.IsValidTextStyleName(
            new string('A', 255)));
        Assert.False(TimberAnnotationTextSettingsRules.IsValidTextStyleName(
            new string('A', 256)));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsInvalidHeightWithoutClamping()
    {
        var invalid = Settings() with
        {
            ItemNumberPaperHeightMm = 3.501d,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(invalid));
    }

    [Fact]
    public void NormalizeStored_PreservesNullLegacyValue()
    {
        Assert.Null(TimberAnnotationTextSettingsRules.NormalizeStored(null));
    }

    [Fact]
    public void NormalizeStored_InvalidFieldsFallBackToFactoryValuesNotBoundaries()
    {
        var invalid = new TimberAnnotationTextSettings(
            "\t",
            11d,
            0.5d,
            double.NaN);

        var normalized = Assert.IsType<TimberAnnotationTextSettings>(
            TimberAnnotationTextSettingsRules.NormalizeStored(invalid));

        Assert.Equal(TimberAnnotationTextSettingsRules.Default, normalized);
        Assert.NotEqual(
            TimberAnnotationTextSettingsRules.MaximumLabelAndDimensionPaperHeightMm,
            normalized.LabelAndDimensionPaperHeightMm);
        Assert.NotEqual(
            TimberAnnotationTextSettingsRules.MinimumItemNumberPaperHeightMm,
            normalized.ItemNumberPaperHeightMm);
    }

    [Theory]
    [InlineData(2.5d, 5, 12.5d)]
    [InlineData(2.5d, 50, 125d)]
    [InlineData(2.5d, 250, 625d)]
    public void CalculateModelHeight_UsesPaperHeightTimesDenominator(
        double paperHeightMm,
        int denominator,
        double expectedModelHeightMm) =>
        Assert.Equal(
            expectedModelHeightMm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                denominator));

    [Theory]
    [InlineData(4)]
    [InlineData(251)]
    public void CalculateModelHeight_RejectsInvalidScaleWithoutNormalization(
        int denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                2.5d,
                denominator));

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CalculateModelHeight_RejectsInvalidPaperHeight(double paperHeightMm) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                50));

    private static TimberAnnotationTextSettings Settings(
        string textStyleName = "Standard") =>
        new(textStyleName, 2.5d, 2.7d, 1.6d);
}
