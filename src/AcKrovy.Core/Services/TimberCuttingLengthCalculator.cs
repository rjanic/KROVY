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
        if (roundingStepMm <= 0)
        {
            return valueMm;
        }

        return Math.Ceiling(valueMm / roundingStepMm) * roundingStepMm;
    }
}
