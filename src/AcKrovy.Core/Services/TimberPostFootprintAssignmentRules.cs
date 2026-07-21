using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintAssignmentRules
{
    public const double DefaultManualLengthMm = 2500d;

    public static TimberElementData CreateMetadata(
        TimberElementData source,
        TimberRectangularFootprintDimensions dimensions)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (dimensions is null)
        {
            throw new ArgumentNullException(nameof(dimensions));
        }

        if (!TimberRectangularFootprintEdgeRules.IsValidEdgeIndex(dimensions.WidthEdgeIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        }

        return source with
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementType = TimberElementType.Post,
            WidthMm = dimensions.WidthMm,
            HeightMm = dimensions.HeightMm,
            FootprintWidthEdgeIndex = dimensions.WidthEdgeIndex,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = source.ManualLengthMm is > 0d
                ? source.ManualLengthMm
                : DefaultManualLengthMm,
        };
    }
}
