namespace AcKrovy.Core.Services;

public static class TimberCuttingLengthCalculator
{
    public const double DefaultRoundingStepMm = 100d;

    public static double Calculate(
        double measuredLengthMm,
        double cuttingAllowanceMm,
        double roundingStepMm = DefaultRoundingStepMm)
    {
        var rawCuttingLengthMm = measuredLengthMm + Math.Max(0, cuttingAllowanceMm);
        return RoundUp(rawCuttingLengthMm, roundingStepMm);
    }

    public static double RoundUp(double valueMm, double roundingStepMm)
    {
        if (double.IsNaN(roundingStepMm) || double.IsInfinity(roundingStepMm) || roundingStepMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roundingStepMm), "Krok zaokrúhľovania musí byť kladné číslo.");
        }

        return Math.Ceiling(valueMm / roundingStepMm) * roundingStepMm;
    }
}
