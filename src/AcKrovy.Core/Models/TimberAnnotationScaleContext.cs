namespace AcKrovy.Core.Models;

public enum TimberAnnotationScaleSource
{
    Drawing = 0,
    UserDefault = 1,
    FixedDefault = 2,
}

public sealed record TimberAnnotationScaleContext
{
    public int Denominator { get; }
    public double ScaleFactor { get; }
    public TimberAnnotationScaleSource Source { get; }

    public TimberAnnotationScaleContext(
        int denominator,
        TimberAnnotationScaleSource source)
    {
        Denominator = Services.TimberAnnotationScaleRules.NormalizeDenominator(denominator);
        ScaleFactor = Services.TimberAnnotationScaleRules.GetScaleFactor(Denominator);
        Source = source;
    }

    public double ScaleLength(double baseLengthMm) =>
        baseLengthMm * ScaleFactor;
}
