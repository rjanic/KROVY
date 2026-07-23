using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>Výpočty dĺžok a objemov. Nezávislé od AutoCADu, preto sa dajú testovať samostatne.</summary>
public static class TimberCalculator
{
    public const double CuttingLengthRoundingIncrementMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm;
    public const double MaximumSlopeDegreesExclusive = 89.9d;

    public static TimberElementMeasurement Measure(
        TimberElementData data,
        double? planLengthMm,
        double roundingIncrementMm = CuttingLengthRoundingIncrementMm)
    {
        ValidateDimension(data.WidthMm, nameof(data.WidthMm));
        ValidateDimension(data.HeightMm, nameof(data.HeightMm));

        var actualLengthMm = CalculateActualLengthMm(data, planLengthMm);
        var cuttingLengthMm = TimberCuttingLengthCalculator.Calculate(
            actualLengthMm,
            data.CuttingAllowanceMm,
            roundingIncrementMm);
        var volumeM3 = CalculateVolumeM3(data.WidthMm, data.HeightMm, cuttingLengthMm);

        return new TimberElementMeasurement(data, planLengthMm, actualLengthMm, cuttingLengthMm, volumeM3);
    }

    public static double CalculateActualLengthMm(TimberElementData data, double? planLengthMm)
    {
        ValidateSlopeDegrees(data.SlopeDegrees);
        var mode = ResolveLengthCalculationMode(data);

        return mode switch
        {
            LengthCalculationMode.PlanLength => RequirePlanLength(planLengthMm),
            LengthCalculationMode.ManualLength => data.ManualLengthMm is > 0
                ? data.ManualLengthMm.Value
                : RequirePlanLength(planLengthMm),
            LengthCalculationMode.SlopeCorrected => CalculateSlopeCorrectedLengthMm(
                RequirePlanLength(planLengthMm),
                data.SlopeDegrees),
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
            TimberElementType.Custom => LengthCalculationMode.SlopeCorrected,
            TimberElementType.Post => LengthCalculationMode.ManualLength,
            _ => LengthCalculationMode.PlanLength,
        };
    }

    public static double CalculateSlopeCorrectedLengthMm(double planLengthMm, double slopeDegrees)
    {
        ValidateSlopeDegrees(slopeDegrees);

        var radians = slopeDegrees * Math.PI / 180.0;
        return planLengthMm / Math.Cos(radians);
    }

    public static bool IsValidSlopeDegrees(double slopeDegrees) =>
        !double.IsNaN(slopeDegrees) &&
        !double.IsInfinity(slopeDegrees) &&
        slopeDegrees >= 0d &&
        slopeDegrees < MaximumSlopeDegreesExclusive;

    public static bool TryValidateSlopeDegrees(double slopeDegrees, out string error)
    {
        if (IsValidSlopeDegrees(slopeDegrees))
        {
            error = string.Empty;
            return true;
        }

        error = "Sklon musí byť nezáporné číslo menšie než 89,9°.";
        return false;
    }

    public static void ValidateSlopeDegrees(double slopeDegrees)
    {
        if (!TryValidateSlopeDegrees(slopeDegrees, out var error))
        {
            throw new ArgumentOutOfRangeException(nameof(slopeDegrees), error);
        }
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
        return TimberCuttingLengthCalculator.RoundUp(valueMm, incrementMm);
    }

    private static void ValidateDimension(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Rozmer musí byť kladné číslo.");
        }
    }

    private static double RequirePlanLength(double? planLengthMm)
    {
        if (!planLengthMm.HasValue)
        {
            throw new InvalidOperationException("Pôdorysná dĺžka nie je pre tento spôsob výpočtu dostupná.");
        }

        ValidateDimension(planLengthMm.Value, nameof(planLengthMm));
        return planLengthMm.Value;
    }
}
