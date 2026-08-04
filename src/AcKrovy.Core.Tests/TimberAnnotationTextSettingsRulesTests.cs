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

        Assert.Equal("Standard", settings.ItemCodeTextStyleName);
        Assert.Equal("Standard", settings.DimensionTextStyleName);
        Assert.Equal("Standard", settings.SlopeTextStyleName);
        Assert.Equal(2.7d, settings.ItemCodePaperHeightMm);
        Assert.Equal(2.5d, settings.DimensionPaperHeightMm);
        Assert.Equal(1.6d, settings.SlopePaperHeightMm);
        Assert.True(settings.HasSharedTextStyleName);
        Assert.True(TimberAnnotationTextSettingsRules.IsValid(settings));
    }

    [Fact]
    public void Settings_RecordUsesValueEqualityAndWithDoesNotMutateSource()
    {
        var source = Settings("ISOCP");
        var equal = Settings("ISOCP");

        var changed = source with { ItemCodePaperHeightMm = 3.1d };

        Assert.Equal(source, equal);
        Assert.NotSame(source, equal);
        Assert.Equal(2.7d, source.ItemCodePaperHeightMm);
        Assert.Equal(3.1d, changed.ItemCodePaperHeightMm);
        Assert.NotEqual(source, changed);
    }

    [Fact]
    public void Settings_RolesAreIndependentAcrossStylesAndHeights()
    {
        var settings = new TimberAnnotationTextSettings(
            "ARIAL",
            "ISOCP",
            "ROMANS",
            2.7d,
            2.5d,
            1.6d);

        Assert.Equal("ARIAL", settings.ItemCodeTextStyleName);
        Assert.Equal("ISOCP", settings.DimensionTextStyleName);
        Assert.Equal("ROMANS", settings.SlopeTextStyleName);
        Assert.False(settings.HasSharedTextStyleName);
        Assert.Equal(
            "ISOCP",
            settings.GetTextStyleName(TimberAnnotationTextRole.Dimension));
        Assert.Equal(
            1.6d,
            settings.GetPaperHeightMm(TimberAnnotationTextRole.Slope));

        var changed = settings.WithRole(
            TimberAnnotationTextRole.Dimension,
            "TIMES",
            3d);

        Assert.Equal("TIMES", changed.DimensionTextStyleName);
        Assert.Equal(3d, changed.DimensionPaperHeightMm);
        Assert.Equal(settings.ItemCodeTextStyleName, changed.ItemCodeTextStyleName);
        Assert.Equal(settings.ItemCodePaperHeightMm, changed.ItemCodePaperHeightMm);
        Assert.Equal(settings.SlopeTextStyleName, changed.SlopeTextStyleName);
        Assert.Equal(settings.SlopePaperHeightMm, changed.SlopePaperHeightMm);
    }

    [Fact]
    public void LegacyTypographyDefaultsDeriveFromCentralPaperDefaults()
    {
        Assert.Equal(
            TimberDimensionTypographyRules.BaseDimensionTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
        Assert.Equal(
            TimberItemNumberTypographyRules.BaseItemNumberTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
        Assert.Equal(
            TimberSlopeAnnotationPresentationRules.BaseTextHeightAtScale50Mm,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm,
                TimberAnnotationScaleRules.DefaultDenominator));
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(10d)]
    public void DimensionHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules
                .IsValidDimensionPaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(10.001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DimensionHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules
                .IsValidDimensionPaperHeightMm(value));

    [Theory]
    [InlineData(1d)]
    [InlineData(3.5d)]
    public void ItemCodeHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules
                .IsValidItemCodePaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(3.501d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ItemCodeHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules
                .IsValidItemCodePaperHeightMm(value));

    [Theory]
    [InlineData(1d)]
    [InlineData(5d)]
    public void SlopeHeight_AcceptsInclusiveBoundaries(double value) =>
        Assert.True(
            TimberAnnotationTextSettingsRules.IsValidSlopePaperHeightMm(value));

    [Theory]
    [InlineData(0.999d)]
    [InlineData(5.001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SlopeHeight_RejectsInvalidValues(double value) =>
        Assert.False(
            TimberAnnotationTextSettingsRules.IsValidSlopePaperHeightMm(value));

    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode, 3.5d, 3.501d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 10d, 10.001d)]
    [InlineData(TimberAnnotationTextRole.Slope, 5d, 5.001d)]
    public void RoleHeightRanges_StayIndependentPerRole(
        TimberAnnotationTextRole role,
        double maximum,
        double aboveMaximum)
    {
        Assert.Equal(
            maximum,
            TimberAnnotationTextSettingsRules.GetMaximumPaperHeightMm(role));
        Assert.True(
            TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(role, maximum));
        Assert.False(
            TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(
                role,
                aboveMaximum));
        Assert.False(
            TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(
                role,
                double.NaN));
        Assert.False(
            TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(
                role,
                double.PositiveInfinity));
    }

    [Fact]
    public void ValidateAndNormalize_TrimsEveryRoleStyleNameWithoutChangingHeights()
    {
        var source = new TimberAnnotationTextSettings(
            "  ARIAL  ",
            "  ISOCP  ",
            "  ROMANS  ",
            2.7d,
            2.5d,
            1.6d);

        var normalized =
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(source);

        Assert.Equal("ARIAL", normalized.ItemCodeTextStyleName);
        Assert.Equal("ISOCP", normalized.DimensionTextStyleName);
        Assert.Equal("ROMANS", normalized.SlopeTextStyleName);
        Assert.Equal(source.ItemCodePaperHeightMm, normalized.ItemCodePaperHeightMm);
        Assert.Equal(source.DimensionPaperHeightMm, normalized.DimensionPaperHeightMm);
        Assert.Equal(source.SlopePaperHeightMm, normalized.SlopePaperHeightMm);
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
    public void ValidateAndNormalize_RejectsInvalidStyleNameOnAnySingleRole()
    {
        var invalidDimensionRole = Settings() with
        {
            DimensionTextStyleName = "  ",
        };

        Assert.Throws<ArgumentException>(() =>
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(
                invalidDimensionRole));
    }

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
            ItemCodePaperHeightMm = 3.501d,
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
    public void NormalizeStored_MigratesOnlyLegacyArchitecturalName()
    {
        var normalized = TimberAnnotationTextSettingsRules.NormalizeStored(
            new TimberAnnotationTextSettings(
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
                TimberAnnotationTextStylePresetRules.LegacyArchitecturalStyleName,
                TimberAnnotationTextStylePresetRules.ArialStyleName,
                2.7d,
                2.5d,
                1.6d));

        Assert.NotNull(normalized);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            normalized.ItemCodeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            normalized.DimensionTextStyleName);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.ArialStyleName,
            normalized.SlopeTextStyleName);
    }

    [Fact]
    public void NormalizeStored_InvalidFieldsFallBackToFactoryValuesNotBoundaries()
    {
        var invalid = TimberAnnotationTextSettings.Shared(
            "\t",
            0.5d,
            11d,
            double.NaN);

        var normalized = Assert.IsType<TimberAnnotationTextSettings>(
            TimberAnnotationTextSettingsRules.NormalizeStored(invalid));

        Assert.Equal(TimberAnnotationTextSettingsRules.Default, normalized);
        Assert.NotEqual(
            TimberAnnotationTextSettingsRules.MaximumDimensionPaperHeightMm,
            normalized.DimensionPaperHeightMm);
        Assert.NotEqual(
            TimberAnnotationTextSettingsRules.MinimumItemCodePaperHeightMm,
            normalized.ItemCodePaperHeightMm);
    }

    [Fact]
    public void NormalizeStored_InvalidRoleStyleFallsBackToItemCodeStyle()
    {
        var partiallyInvalid = new TimberAnnotationTextSettings(
            " ISOCP ",
            "\t",
            null!,
            2.7d,
            2.5d,
            1.6d);

        var normalized = Assert.IsType<TimberAnnotationTextSettings>(
            TimberAnnotationTextSettingsRules.NormalizeStored(partiallyInvalid));

        Assert.Equal("ISOCP", normalized.ItemCodeTextStyleName);
        Assert.Equal("ISOCP", normalized.DimensionTextStyleName);
        Assert.Equal("ISOCP", normalized.SlopeTextStyleName);
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
        TimberAnnotationTextSettings.Shared(textStyleName, 2.7d, 2.5d, 1.6d);
}
