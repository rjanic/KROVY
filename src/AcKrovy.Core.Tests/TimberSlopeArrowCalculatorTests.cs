using System.Text.Json;
using System.Text.Json.Serialization;
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

        Assert.Equal(350, placement.TailX);
        Assert.Equal(-180, placement.TailY);
        Assert.Equal(650, placement.TipX);
        Assert.Equal(-180, placement.TipY);
        Assert.True(placement.TipX > placement.TailX);
    }

    [Fact]
    public void Calculate_ReversedDirectionPointsFromEndToStartWithoutMovingCenter()
    {
        var normal = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: false);
        var reversed = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: true);

        Assert.Equal(normal.TailX, reversed.TipX);
        Assert.Equal(normal.TipX, reversed.TailX);
        Assert.Equal(normal.TailY, reversed.TipY);
        Assert.Equal(normal.TipY, reversed.TailY);
        Assert.True(reversed.TipX < reversed.TailX);
    }

    [Fact]
    public void Calculate_EndpointOrderKeepsArrowOnSideOppositeUprightLabel()
    {
        var forward = TimberSlopeArrowCalculator.Calculate(0, 0, 1000, 0, 500, 0, isReversed: false);
        var backward = TimberSlopeArrowCalculator.Calculate(1000, 0, 0, 0, 500, 0, isReversed: false);

        Assert.Equal(forward.TailY, backward.TailY);
        Assert.Equal(forward.TipY, backward.TipY);
        Assert.Equal(-TimberSlopeArrowCalculator.OffsetMm, forward.TailY);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void ShouldDisplay_UsesPositiveSlopeOnly(double slopeDegrees, bool expected)
    {
        Assert.Equal(expected, TimberSlopeArrowCalculator.ShouldDisplay(slopeDegrees));
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
