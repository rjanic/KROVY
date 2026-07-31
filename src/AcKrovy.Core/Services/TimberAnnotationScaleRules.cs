namespace AcKrovy.Core.Services;

public static class TimberAnnotationScaleRules
{
    public const int DefaultDenominator = 50;
    public const int MinimumDenominator = 10;
    public const int MaximumDenominator = 200;

    public static bool IsValidDenominator(int denominator) =>
        denominator >= MinimumDenominator &&
        denominator <= MaximumDenominator;

    public static int NormalizeDenominator(int denominator) =>
        IsValidDenominator(denominator)
            ? denominator
            : DefaultDenominator;

    public static double GetScaleFactor(int denominator) =>
        NormalizeDenominator(denominator) / (double)DefaultDenominator;

    public static double ScaleLength(double baseLengthMm, int denominator) =>
        baseLengthMm * GetScaleFactor(denominator);
}
