using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>Výpočty dĺžok a objemov. Nezávislé od AutoCADu, preto sa dajú testovať samostatne.</summary>
public static class TimberCalculator
{
    public const double CuttingLengthRoundingIncrementMm = 100d;

    public static TimberElementMeasurement Measure(
        TimberElementData data,
        double planLengthMm,
        double roundingIncrementMm = CuttingLengthRoundingIncrementMm)
    {
        ValidateDimension(data.WidthMm, nameof(data.WidthMm));
        ValidateDimension(data.HeightMm, nameof(data.HeightMm));
        ValidateDimension(planLengthMm, nameof(planLengthMm));

        var actualLengthMm = CalculateActualLengthMm(data, planLengthMm);
        var cuttingLengthMm = RoundUp(actualLengthMm + Math.Max(0, data.CuttingAllowanceMm), roundingIncrementMm);
        var volumeM3 = CalculateVolumeM3(data.WidthMm, data.HeightMm, cuttingLengthMm);

        return new TimberElementMeasurement(data, planLengthMm, actualLengthMm, cuttingLengthMm, volumeM3);
    }

    public static double CalculateActualLengthMm(TimberElementData data, double planLengthMm)
    {
        var mode = ResolveLengthCalculationMode(data);

        return mode switch
        {
            LengthCalculationMode.PlanLength => planLengthMm,
            LengthCalculationMode.ManualLength => data.ManualLengthMm is > 0
                ? data.ManualLengthMm.Value
                : planLengthMm,
            LengthCalculationMode.SlopeCorrected => CalculateSlopeCorrectedLengthMm(planLengthMm, data.SlopeDegrees),
            _ => throw new InvalidOperationException($"Nepodporovaný režim výpočtu: {mode}."),
        };
    }

    public static LengthCalculationMode ResolveLengthCalculationMode(TimberElementData data)
    {
        if (data.LengthCalculationMode != LengthCalculationMode.AutoByElementType)
        {
            return data.LengthCalculationMode;
        }

        return data.ElementType switch
        {
            TimberElementType.Rafter => LengthCalculationMode.SlopeCorrected,
            TimberElementType.Brace => LengthCalculationMode.SlopeCorrected,
            TimberElementType.Post => LengthCalculationMode.ManualLength,
            _ => LengthCalculationMode.PlanLength,
        };
    }

    public static double CalculateSlopeCorrectedLengthMm(double planLengthMm, double slopeDegrees)
    {
        if (slopeDegrees is <= 0 or >= 89.9)
        {
            throw new ArgumentOutOfRangeException(nameof(slopeDegrees), "Sklon musí byť väčší než 0° a menší než 89,9°.");
        }

        var radians = slopeDegrees * Math.PI / 180.0;
        return planLengthMm / Math.Cos(radians);
    }

    public static double CalculateVolumeM3(double widthMm, double heightMm, double lengthMm)
    {
        ValidateDimension(widthMm, nameof(widthMm));
        ValidateDimension(heightMm, nameof(heightMm));
        ValidateDimension(lengthMm, nameof(lengthMm));

        return widthMm * heightMm * lengthMm / 1_000_000_000d;
    }

    public static double RoundUp(double valueMm, double incrementMm)
    {
        if (incrementMm <= 0)
        {
            return valueMm;
        }

        return Math.Ceiling(valueMm / incrementMm) * incrementMm;
    }

    private static void ValidateDimension(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Rozmer musí byť kladné číslo.");
        }
    }
}
