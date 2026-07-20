using Autodesk.AutoCAD.DatabaseServices;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadEntityHelpers
{
    public static bool IsSupportedTimberGeometry(Entity entity) => entity is Line or Polyline;

    public static double GetPlanLengthMm(Entity entity) => entity switch
    {
        Line line => line.Length,
        Polyline polyline => polyline.Length,
        _ => throw new NotSupportedException(UiStrings.ErrorUnsupportedTimberGeometry),
    };
}
