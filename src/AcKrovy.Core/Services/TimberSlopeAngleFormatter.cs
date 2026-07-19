using System.Globalization;

namespace AcKrovy.Core.Services;

public static class TimberSlopeAngleFormatter
{
    public static string Format(double slopeDegrees, CultureInfo? culture = null) =>
        $"{slopeDegrees.ToString("0.###", culture ?? CultureInfo.CurrentCulture)}°";
}
