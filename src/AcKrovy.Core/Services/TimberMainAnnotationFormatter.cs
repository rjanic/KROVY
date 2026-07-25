using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberMainAnnotationFormatter
{
    public static string Format(
        TimberElementData data,
        TimberElementMeasurement measurement,
        TimberElementLabelFormatOptions? options = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (measurement is null)
        {
            throw new ArgumentNullException(nameof(measurement));
        }

        return TimberAnnotationModeRules.Normalize(data.AnnotationMode) switch
        {
            TimberAnnotationMode.FullLabel =>
                TimberElementLabelFormatter.Format(data, measurement, options),
            TimberAnnotationMode.ItemNumberLeader => data.ElementId,
            TimberAnnotationMode.DimensionsLeader =>
                TimberElementLabelFormatter.FormatDimensions(data, options),
            _ => throw new InvalidOperationException(),
        };
    }
}
