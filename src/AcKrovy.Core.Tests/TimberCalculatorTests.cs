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

    [Fact]
    public void CalculateSlopeCorrectedLength_AllowsZeroSlope()
    {
        Assert.Equal(1000d, TimberCalculator.CalculateSlopeCorrectedLengthMm(1000d, 0d));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(89.9)]
    [InlineData(90)]
    public void CalculateSlopeCorrectedLength_RejectsOutOfRangeSlope(double slopeDegrees)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCalculator.CalculateSlopeCorrectedLengthMm(1000, slopeDegrees));
    }

    [Theory]
    [InlineData(0d, true)]
    [InlineData(30d, true)]
    [InlineData(45.5d, true)]
    [InlineData(89.8d, true)]
    [InlineData(-0.1d, false)]
    [InlineData(89.9d, false)]
    public void SlopeValidation_UsesZeroInclusiveUpperExclusiveRange(double slopeDegrees, bool expected)
    {
        Assert.Equal(expected, TimberCalculator.IsValidSlopeDegrees(slopeDegrees));
    }

    [Fact]
    public void ChangingWallPlateToRafterAtZeroSlopeMeasuresWithoutException()
    {
        var wallPlate = new TimberElementData
        {
            ElementType = TimberElementType.WallPlate,
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
            SlopeDegrees = 0d,
            WidthMm = 160d,
            HeightMm = 160d,
        };
        var patch = new TimberElementPatch(
            TimberElementType.Rafter,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var rafter = TimberElementPatcher.Apply(wallPlate, patch);
        var measurement = TimberCalculator.Measure(rafter, 4000d);

        Assert.Equal(LengthCalculationMode.SlopeCorrected, TimberCalculator.ResolveLengthCalculationMode(rafter));
        Assert.Equal(4000d, measurement.ActualLengthMm);
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

    [Theory]
    [InlineData(4800, 4800)]
    [InlineData(4801, 4900)]
    [InlineData(4899, 4900)]
    [InlineData(4900, 4900)]
    [InlineData(4901, 5000)]
    public void Measure_RoundsCuttingLengthUpToHundredMillimeters(double planLengthMm, double expectedCuttingLengthMm)
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 0,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm);

        Assert.Equal(expectedCuttingLengthMm, measurement.CuttingLengthMm);
    }

    [Fact]
    public void Measure_AddsCuttingAllowanceBeforeHundredMillimeterRounding()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 85,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm: 11000);

        Assert.Equal(11100, measurement.CuttingLengthMm);
    }

    [Fact]
    public void Measure_GeometryChangeUpdatesActualAndCuttingLengthInAutomaticMode()
    {
        var data = new TimberElementData
        {
            ElementType = TimberElementType.Purlin,
            LengthCalculationMode = LengthCalculationMode.AutoByElementType,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 100,
        };

        var before = TimberCalculator.Measure(data, planLengthMm: 5000);
        var after = TimberCalculator.Measure(data, planLengthMm: 5350);

        Assert.Equal(5000, before.ActualLengthMm);
        Assert.Equal(5100, before.CuttingLengthMm);
        Assert.Equal(5350, after.ActualLengthMm);
        Assert.Equal(5500, after.CuttingLengthMm);
        Assert.Equal(100, after.Data.CuttingAllowanceMm);
    }

    [Fact]
    public void Measure_GeometryChangeDoesNotOverrideManualLengthMode()
    {
        var data = new TimberElementData
        {
            ElementType = TimberElementType.Post,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 100,
        };

        var before = TimberCalculator.Measure(data, planLengthMm: 1000);
        var after = TimberCalculator.Measure(data, planLengthMm: 5000);

        Assert.Equal(2500, before.ActualLengthMm);
        Assert.Equal(2600, before.CuttingLengthMm);
        Assert.Equal(2500, after.ActualLengthMm);
        Assert.Equal(2600, after.CuttingLengthMm);
        Assert.Equal(100, after.Data.CuttingAllowanceMm);
    }

    [Fact]
    public void Measure_AllowsZeroCuttingAllowance()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = 0,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm: 11001);

        Assert.Equal(11100, measurement.CuttingLengthMm);
    }

    [Fact]
    public void Measure_TreatsNegativeCuttingAllowanceAsZero()
    {
        var data = new TimberElementData
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            WidthMm = 100,
            HeightMm = 100,
            CuttingAllowanceMm = -500,
        };

        var measurement = TimberCalculator.Measure(data, planLengthMm: 11001);

        Assert.Equal(11100, measurement.CuttingLengthMm);
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
    public void RoundUp_UsesExistingIncrementBehavior(double valueMm, double incrementMm, double expected)
    {
        Assert.Equal(expected, TimberCalculator.RoundUp(valueMm, incrementMm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void RoundUp_RejectsInvalidIncrement(double incrementMm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberCalculator.RoundUp(1001, incrementMm));
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
