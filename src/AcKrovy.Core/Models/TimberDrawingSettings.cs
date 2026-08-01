using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public sealed record TimberDrawingSettings
{
    public const int DrawingSettingsSchemaVersion = 1;

    public int SchemaVersion { get; init; } = DrawingSettingsSchemaVersion;
    public int AnnotationScaleDenominator { get; init; } =
        TimberAnnotationScaleRules.DefaultDenominator;

    public static TimberDrawingSettings Create(int annotationScaleDenominator)
    {
        if (!TimberAnnotationScaleRules.IsValidDenominator(annotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(annotationScaleDenominator),
                annotationScaleDenominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }

        return new TimberDrawingSettings
        {
            AnnotationScaleDenominator = annotationScaleDenominator,
        };
    }

    public static bool TryFromStoredValues(
        int schemaVersion,
        int annotationScaleDenominator,
        out TimberDrawingSettings? settings)
    {
        settings = null;
        if (schemaVersion != DrawingSettingsSchemaVersion ||
            !TimberAnnotationScaleRules.IsValidDenominator(annotationScaleDenominator))
        {
            return false;
        }

        settings = new TimberDrawingSettings
        {
            SchemaVersion = schemaVersion,
            AnnotationScaleDenominator = annotationScaleDenominator,
        };
        return true;
    }
}
