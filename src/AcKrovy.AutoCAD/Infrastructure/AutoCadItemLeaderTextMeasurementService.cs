using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadItemLeaderTextMeasurementResult(
    bool Succeeded,
    double MeasuredWidthMm,
    double DefinitionTextHeightMm,
    string DiagnosticReason);

internal static class AutoCadItemLeaderTextMeasurementService
{
    public static AutoCadItemLeaderTextMeasurementResult Measure(
        Database database,
        ObjectId resolvedTextStyleId,
        double itemNumberPaperHeightMm,
        string itemText)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (string.IsNullOrWhiteSpace(itemText))
        {
            return Failure("A non-empty ITEM_NO token is required.");
        }
        if (!AutoCadDatabaseIdentity.IsSame(database, resolvedTextStyleId))
        {
            return Failure(
                "Resolved text-style ObjectId belongs to a different database.");
        }

        double definitionTextHeightMm;
        try
        {
            definitionTextHeightMm =
                TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                    itemNumberPaperHeightMm,
                    TimberAnnotationScaleRules.DefaultDenominator);
        }
        catch (System.Exception exception)
        {
            return Failure(exception.Message);
        }

        try
        {
            using var text = new DBText();
            text.SetDatabaseDefaults(database);
            text.Position = Point3d.Origin;
            text.TextStyleId = resolvedTextStyleId;
            text.Height = definitionTextHeightMm;
            text.TextString = itemText;
            text.AdjustAlignment(database);
            var extents = text.GeometricExtents;
            var width = extents.MaxPoint.X - extents.MinPoint.X;
            if (!double.IsFinite(width) || width < 0d)
            {
                return Failure(
                    "AutoCAD returned a non-finite ITEM_NO text width.",
                    definitionTextHeightMm);
            }

            return new AutoCadItemLeaderTextMeasurementResult(
                true,
                width,
                definitionTextHeightMm,
                "Measured a transient DBText using the resolved text style and definition height.");
        }
        catch (System.Exception exception)
        {
            return Failure(
                $"AutoCAD could not measure ITEM_NO text: {exception.Message}",
                definitionTextHeightMm);
        }
    }

    private static AutoCadItemLeaderTextMeasurementResult Failure(
        string reason,
        double definitionTextHeightMm = 0d) =>
        new(false, 0d, definitionTextHeightMm, reason);
}
