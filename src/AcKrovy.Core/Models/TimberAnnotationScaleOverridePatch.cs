using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public enum TimberAnnotationScaleOverrideChange
{
    Unchanged = 0,
    Set = 1,
    Clear = 2,
}

public sealed record TimberAnnotationScaleOverridePatch
{
    public static TimberAnnotationScaleOverridePatch Unchanged { get; } =
        new(TimberAnnotationScaleOverrideChange.Unchanged, null);

    public static TimberAnnotationScaleOverridePatch Clear { get; } =
        new(TimberAnnotationScaleOverrideChange.Clear, null);

    public TimberAnnotationScaleOverrideChange Change { get; }
    public int? Denominator { get; }

    private TimberAnnotationScaleOverridePatch(
        TimberAnnotationScaleOverrideChange change,
        int? denominator)
    {
        Change = change;
        Denominator = denominator;
    }

    public static TimberAnnotationScaleOverridePatch Set(int denominator)
    {
        if (!TimberAnnotationScaleRules.IsValidDenominator(denominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator),
                denominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }

        return new TimberAnnotationScaleOverridePatch(
            TimberAnnotationScaleOverrideChange.Set,
            denominator);
    }
}
