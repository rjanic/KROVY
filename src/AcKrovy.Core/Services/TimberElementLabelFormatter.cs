using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabelFormatter
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

        var formatOptions = options ?? TimberElementLabelFormatOptions.Default;
        return $"{data.ElementId}\\P{FormatDimensions(data, formatOptions)}\\P{measurement.CuttingLengthMm:0} mm";
    }

    public static string FormatDimensions(
        TimberElementData data,
        TimberElementLabelFormatOptions? options = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var formatOptions = options ?? TimberElementLabelFormatOptions.Default;
        return $"{data.WidthMm:0}{formatOptions.DimensionSeparator}{data.HeightMm:0}";
    }
}
