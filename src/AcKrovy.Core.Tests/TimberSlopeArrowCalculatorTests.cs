using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberSlopeArrowCalculatorTests
{
    [Fact]
    public void Calculate_NormalDirectionPointsFromStartToEnd()
    {
        var placement = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: false);

        Assert.Equal(560, placement.TipX);
        Assert.Equal(0, placement.TipY);
        Assert.Equal(440, placement.HeadLeftX);
        Assert.Equal(50, placement.HeadLeftY);
        Assert.Equal(440, placement.HeadRightX);
        Assert.Equal(-50, placement.HeadRightY);
    }

    [Fact]
    public void Calculate_ReversedDirectionTurnsArrowAroundSameMidpoint()
    {
        var normal = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: false);
        var reversed = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: true);

        Assert.Equal(560, normal.TipX);
        Assert.Equal(440, reversed.TipX);
        Assert.Equal(500, (normal.TipX + normal.HeadLeftX) / 2d);
        Assert.Equal(500, (reversed.TipX + reversed.HeadLeftX) / 2d);
    }

    [Fact]
    public void Calculate_VerticalElementKeepsTipAndHeadCenteredOnElementAxis()
    {
        var placement = TimberSlopeArrowCalculator.Calculate(0, 0, 0, 1000, 0, 500, isReversed: false);

        Assert.Equal(0, placement.TipX);
        Assert.Equal(560, placement.TipY);
        Assert.Equal(440, placement.HeadLeftY);
        Assert.Equal(440, placement.HeadRightY);
    }

    [Fact]
    public void Calculate_VeryShortElementProducesStableFixedSizeMarkerAtMidpoint()
    {
        var placement = TimberSlopeArrowCalculator.Calculate(0, 0, 40, 0, 20, 0, isReversed: false);

        Assert.Equal(TimberSlopeArrowCalculator.HeadLengthMm, placement.TipX - placement.HeadLeftX);
        Assert.Equal(20, (placement.TipX + placement.HeadLeftX) / 2d);
    }

    [Fact]
    public void CalculatePosition_UsesOneThirdOfElementLength()
    {
        var position = TimberSlopeArrowCalculator.CalculatePosition(0, 0, 900, 300);

        Assert.Equal(300, position.X);
        Assert.Equal(100, position.Y);
        Assert.Equal(1d / 3d, TimberSlopeArrowCalculator.SlopeAnnotationPositionFactor);
    }

    [Theory]
    [InlineData(0, "0°")]
    [InlineData(30, "30°")]
    [InlineData(35, "35°")]
    [InlineData(35.5, "35,5°")]
    public void AngleFormatter_UsesCultureAndOmitsTrailingZeros(double slopeDegrees, string expected)
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");

        Assert.Equal(expected, TimberSlopeAngleFormatter.Format(slopeDegrees, culture));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ToggleDirection_InvertsOnlyDirection(bool current, bool expected)
    {
        Assert.Equal(expected, TimberSlopeAnnotationRules.ToggleDirection(current));
    }

    [Theory]
    [InlineData("A1", "A1", true)]
    [InlineData("a1", " A1 ", true)]
    [InlineData("A1", "B2", false)]
    public void SourceHandleBinding_UsesPhysicalTimberHandle(
        string annotationHandle,
        string timberHandle,
        bool expected)
    {
        Assert.Equal(
            expected,
            TimberSlopeAnnotationRules.HasSameSourceHandle(annotationHandle, timberHandle));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void ShouldDisplay_UsesPositiveSlopeOnly(double slopeDegrees, bool expected)
    {
        Assert.Equal(expected, TimberSlopeArrowCalculator.ShouldDisplay(slopeDegrees));
    }

    [Theory]
    [InlineData(0d, TimberSlopeGlyphKind.HorizontalMarker)]
    [InlineData(30d, TimberSlopeGlyphKind.DirectionalArrow)]
    [InlineData(-1d, TimberSlopeGlyphKind.None)]
    public void GlyphKind_ChoosesMarkerForZeroAndArrowForPositiveSlope(
        double slopeDegrees,
        TimberSlopeGlyphKind expected)
    {
        Assert.Equal(expected, TimberSlopeAnnotationRules.ResolveGlyphKind(slopeDegrees));
    }

    [Fact]
    public void ChangingBetweenZeroAndPositiveSlopeSwitchesGlyphKind()
    {
        var marker = TimberSlopeAnnotationRules.ResolveGlyphKind(0d);
        var arrow = TimberSlopeAnnotationRules.ResolveGlyphKind(30d);
        var markerAgain = TimberSlopeAnnotationRules.ResolveGlyphKind(0d);

        Assert.Equal(TimberSlopeGlyphKind.HorizontalMarker, marker);
        Assert.Equal(TimberSlopeGlyphKind.DirectionalArrow, arrow);
        Assert.Equal(TimberSlopeGlyphKind.HorizontalMarker, markerAgain);
    }

    [Theory]
    [InlineData(0d, false)]
    [InlineData(30d, true)]
    public void FlipDirection_IsAvailableOnlyForPositiveSlope(double slopeDegrees, bool expected)
    {
        Assert.Equal(expected, TimberSlopeAnnotationRules.CanFlipDirection(slopeDegrees));
    }

    [Fact]
    public void OldMetadataWithoutDirectionDefaultsToNormalDirection()
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 90,
              "HeightMm": 170,
              "SlopeDegrees": 37,
              "RoofPlaneId": "R3",
              "CuttingAllowanceMm": 120,
              "LengthCalculationMode": "SlopeCorrected",
              "Material": "Smrek C24"
            }
            """;
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };

        var data = JsonSerializer.Deserialize<TimberElementData>(json, options);

        Assert.NotNull(data);
        Assert.False(data!.IsSlopeDirectionReversed);
        Assert.Equal(120, data.CuttingAllowanceMm);
        Assert.Equal("K9", data.ElementId);
    }

    [Fact]
    public void ReversingDirectionDoesNotChangeMeasurementOrSignature()
    {
        var normal = new TimberElementData
        {
            ElementId = "K4",
            ElementType = TimberElementType.Rafter,
            WidthMm = 80,
            HeightMm = 160,
            SlopeDegrees = 30,
            CuttingAllowanceMm = 200,
            LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
        };
        var reversed = normal with { IsSlopeDirectionReversed = true };

        var normalMeasurement = TimberCalculator.Measure(normal, 4000);
        var reversedMeasurement = TimberCalculator.Measure(reversed, 4000);

        Assert.Equal(normalMeasurement.ActualLengthMm, reversedMeasurement.ActualLengthMm);
        Assert.Equal(normalMeasurement.CuttingLengthMm, reversedMeasurement.CuttingLengthMm);
        Assert.Equal(normal.CuttingAllowanceMm, reversed.CuttingAllowanceMm);
        Assert.Equal(normal.ElementId, reversed.ElementId);
        Assert.Equal(
            TimberElementSignature.FromMeasurement(normalMeasurement),
            TimberElementSignature.FromMeasurement(reversedMeasurement));
    }
}
