using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberCalculatorTests
{
    [Fact]
    public void Measure_UsesPlanLengthWithoutSlope()
    {
        var data = new TimberElementData
        {
            ElementType = TimberElementType.Purlin,
            WidthMm = 100,
            HeightMm = 200,
            CuttingAllowanceMm = 0,
            LengthCalculationMode = LengthCalculationMode.PlanLength,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm: 3000);

        Assert.Equal(3000, measurement.PlanLengthMm);
        Assert.Equal(3000, measurement.ActualLengthMm);
        Assert.Equal(3000, measurement.CuttingLengthMm);
    }

    [Fact]
    public void CalculateActualLength_UsesSlopeCorrection()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
            SlopeDegrees = 60,
        };

        var actual = TimberCalculator.CalculateActualLengthMm(data, planLengthMm: 1000);

        Assert.Equal(2000, actual, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(89.9)]
    [InlineData(90)]
    public void CalculateSlopeCorrectedLength_RejectsOutOfRangeSlope(double slopeDegrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCalculator.CalculateSlopeCorrectedLengthMm(1000, slopeDegrees));
    }

    [Fact]
    public void Measure_AddsCuttingAllowanceAndRoundsUp()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 55,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm: 1000, roundingIncrementMm: 10);

        Assert.Equal(1060, measurement.CuttingLengthMm);
    }

    [Fact]
    public void CalculateActualLength_UsesManualLengthWhenPositive()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
        };

        var actual = TimberCalculator.CalculateActualLengthMm(data, planLengthMm: 1000);

        Assert.Equal(2500, actual);
    }

    [Fact]
    public void CalculateActualLength_FallsBackToPlanLengthForMissingManualLength()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = null,
        };

        var actual = TimberCalculator.CalculateActualLengthMm(data, planLengthMm: 1000);

        Assert.Equal(1000, actual);
    }

    [Fact]
    public void CalculateVolume_ComputesCubicMeters()
    {
        var volume = TimberCalculator.CalculateVolumeM3(widthMm: 100, heightMm: 200, lengthMm: 3000);

        Assert.Equal(0.06, volume, precision: 6);
    }

    [Theory]
    [InlineData(0, 100, 1000)]
    [InlineData(100, 0, 1000)]
    [InlineData(100, 100, 0)]
    [InlineData(double.NaN, 100, 1000)]
    [InlineData(100, double.PositiveInfinity, 1000)]
    public void CalculateVolume_RejectsInvalidDimensions(double widthMm, double heightMm, double lengthMm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCalculator.CalculateVolumeM3(widthMm, heightMm, lengthMm));
    }

    [Theory]
    [InlineData(1001, 10, 1010)]
    [InlineData(1000, 10, 1000)]
    [InlineData(1001, 0, 1001)]
    [InlineData(1001, -10, 1001)]
    public void RoundUp_UsesExistingIncrementBehavior(double valueMm, double incrementMm, double expected)
    {
        Assert.Equal(expected, TimberCalculator.RoundUp(valueMm, incrementMm));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, LengthCalculationMode.SlopeCorrected)]
    [InlineData(TimberElementType.Brace, LengthCalculationMode.SlopeCorrected)]
    [InlineData(TimberElementType.Post, LengthCalculationMode.ManualLength)]
    [InlineData(TimberElementType.Purlin, LengthCalculationMode.PlanLength)]
    public void ResolveLengthCalculationMode_UsesCurrentTypeDefaults(
        TimberElementType type,
        LengthCalculationMode expectedMode)
    {
        var data = new TimberElementData
        {
            ElementType = type,
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        };

        Assert.Equal(expectedMode, TimberCalculator.ResolveLengthCalculationMode(data));
    }
}
