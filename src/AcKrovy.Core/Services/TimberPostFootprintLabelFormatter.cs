using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintLabelFormatter
{
    public static string Format(TimberElementData data, double actualLengthMm)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (double.IsNaN(actualLengthMm) || double.IsInfinity(actualLengthMm) || actualLengthMm <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(actualLengthMm));
        }

        return $"{data.ElementId}\\P{data.WidthMm:0}x{data.HeightMm:0}\\P{actualLengthMm:0} mm";
    }
}
