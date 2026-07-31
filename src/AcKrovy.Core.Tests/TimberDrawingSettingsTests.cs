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
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Create_PreservesValidAnnotationScaleDenominator(int denominator) =>
        Assert.Equal(
            denominator,
            TimberDrawingSettings.Create(denominator).AnnotationScaleDenominator);

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(201)]
    [InlineData(-1)]
    public void Create_NormalizesInvalidAnnotationScaleDenominatorToDefault(
        int denominator) =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberDrawingSettings.Create(denominator).AnnotationScaleDenominator);

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
    [InlineData(1, 9)]
    [InlineData(1, 201)]
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
    public void Resolver_MissingDrawingValueUsesUserDefault() =>
        Assert.Equal(
            75,
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
    public void ResolverContext_MissingDrawingReportsUserDefaultSource()
    {
        var context = TimberAnnotationScaleResolver.ResolveContext(
            hasDrawingValue: false,
            drawingDenominator: 100,
            userDefaultDenominator: 25);

        Assert.Equal(25, context.Denominator);
        Assert.Equal(0.5d, context.ScaleFactor);
        Assert.Equal(TimberAnnotationScaleSource.UserDefault, context.Source);
    }

    [Fact]
    public void Resolver_InvalidDrawingValueUsesFactoryDefault() =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: true,
                drawingDenominator: 201,
                userDefaultDenominator: 75));

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(201)]
    [InlineData(-1)]
    public void Resolver_InvalidUserDefaultUsesFactoryDefault(int userDefaultDenominator) =>
        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationScaleResolver.Resolve(
                hasDrawingValue: false,
                drawingDenominator: 100,
                userDefaultDenominator));
}
