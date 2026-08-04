using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadAnnotationPresentationContextTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(250)]
    public void Create_ExplicitSettingsCalculateAllModelHeights(int denominator)
    {
        var settings = TimberAnnotationTextSettings.Shared(
            "Krovy",
            2d,
            3d,
            1.5d);
        var data = Data(settings);

        var context = AutoCadAnnotationPresentationValues.Create(
            Scale(denominator),
            data);

        Assert.True(context.HasExplicitTextSettings);
        Assert.Equal(settings, context.EffectiveTextSettings);
        Assert.Equal(denominator, context.AnnotationScaleDenominator);
        Assert.Equal(3d * denominator, context.LabelAndDimensionModelHeight);
        Assert.Equal(2d * denominator, context.ItemNumberModelHeight);
        Assert.Equal(1.5d * denominator, context.SlopeAngleModelHeight);
    }

    [Fact]
    public void Create_LegacyNullUsesExplicitClassicProductDefault()
    {
        var context = AutoCadAnnotationPresentationValues.Create(
            Scale(50),
            Data(null));

        Assert.False(context.HasExplicitTextSettings);
        Assert.Equal(
            TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings(),
            context.EffectiveTextSettings);
        Assert.Equal(125d, context.LabelAndDimensionModelHeight);
        Assert.Equal(135d, context.ItemNumberModelHeight);
        Assert.Equal(80d, context.SlopeAngleModelHeight);
    }

    [Fact]
    public void Create_InvalidStoredFieldsUseCoreNormalizationWithoutMutation()
    {
        var stored = TimberAnnotationTextSettings.Shared(
            "  Krovy  ",
            99d,
            double.NaN,
            -1d);
        var data = Data(stored);

        var context = AutoCadAnnotationPresentationValues.Create(
            Scale(50),
            data);

        Assert.Equal(
            "Krovy",
            context.EffectiveTextSettings.ItemCodeTextStyleName);
        Assert.Equal(125d, context.LabelAndDimensionModelHeight);
        Assert.Equal(135d, context.ItemNumberModelHeight);
        Assert.Equal(80d, context.SlopeAngleModelHeight);
        Assert.Same(stored, data.AnnotationTextSettings);
        Assert.Equal(
            "  Krovy  ",
            data.AnnotationTextSettings!.ItemCodeTextStyleName);
    }

    [Fact]
    public void PresentationValues_HaveNoPublicConstructorOrSettableState()
    {
        Assert.Empty(typeof(AutoCadAnnotationPresentationValues).GetConstructors());
        Assert.All(
            typeof(AutoCadAnnotationPresentationValues).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    private static TimberAnnotationScaleContext Scale(int denominator) =>
        new(denominator, TimberAnnotationScaleSource.Drawing);

    private static TimberElementData Data(
        TimberAnnotationTextSettings? settings) =>
        new()
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = "E-1",
            AnnotationTextSettings = settings,
        };
}
