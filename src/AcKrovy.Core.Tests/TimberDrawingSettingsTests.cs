using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberDrawingSettingsTests
{
    [Fact]
    public void DrawingSettingsSchemaVersion_IsOne() =>
        Assert.Equal(1, TimberDrawingSettings.DrawingSettingsSchemaVersion);

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(250)]
    public void Create_PreservesValidAnnotationScaleDenominator(int denominator) =>
        Assert.Equal(
            denominator,
            TimberDrawingSettings.Create(denominator).AnnotationScaleDenominator);

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(251)]
    [InlineData(-1)]
    public void Create_RejectsInvalidAnnotationScaleDenominatorWithoutClamping(
        int denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberDrawingSettings.Create(denominator));

    [Fact]
    public void TryFromStoredValues_AcceptsSupportedSchemaAndValidDenominator()
    {
        var success = TimberDrawingSettings.TryFromStoredValues(
            TimberDrawingSettings.DrawingSettingsSchemaVersion,
            100,
            out var settings);

        Assert.True(success);
        Assert.NotNull(settings);
        Assert.Equal(100, settings!.AnnotationScaleDenominator);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(2, 100)]
    [InlineData(1, 4)]
    [InlineData(1, 251)]
    public void TryFromStoredValues_RejectsUnsupportedOrInvalidPayload(
        int schemaVersion,
        int denominator)
    {
        Assert.False(TimberDrawingSettings.TryFromStoredValues(
            schemaVersion,
            denominator,
            out var settings));
        Assert.Null(settings);
    }

    [Fact]
    public void Resolver_PrefersDrawingDenominator() =>
        Assert.Equal(
            100,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: true,
                drawingDenominator: 100,
                userDefaultDenominator: 25));

    [Fact]
    public void Resolver_MissingDrawingValueUsesFixedDefault() =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: false,
                drawingDenominator: 100,
                userDefaultDenominator: 75));

    [Fact]
    public void ResolverContext_ReportsDrawingSourceAndNormalizedFactor()
    {
        var context = TimberAnnotationScaleResolver.ResolveContext(
            hasDrawingValue: true,
            drawingDenominator: 100,
            userDefaultDenominator: 25);

        Assert.Equal(100, context.Denominator);
        Assert.Equal(2d, context.ScaleFactor);
        Assert.Equal(TimberAnnotationScaleSource.Drawing, context.Source);
    }

    [Fact]
    public void ResolverContext_MissingDrawingReportsFixedDefaultSource()
    {
        var context = TimberAnnotationScaleResolver.ResolveContext(
            hasDrawingValue: false,
            drawingDenominator: 100,
            userDefaultDenominator: 25);

        Assert.Equal(50, context.Denominator);
        Assert.Equal(1d, context.ScaleFactor);
        Assert.Equal(TimberAnnotationScaleSource.FixedDefault, context.Source);
    }

    [Fact]
    public void Resolver_InvalidDrawingValueUsesFactoryDefault() =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: true,
                drawingDenominator: 251,
                userDefaultDenominator: 75));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(251)]
    [InlineData(-1)]
    public void Resolver_InvalidUserDefaultUsesFactoryDefault(int userDefaultDenominator) =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: false,
                drawingDenominator: 100,
                userDefaultDenominator));
}
