using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record PostFootprintSelection(
    ObjectId PolylineId,
    TimberRectangularFootprintGeometry Geometry,
    TimberRectangularFootprintDimensions Dimensions);

internal static class PostFootprintSelectionService
{
    public static bool TryPrompt(Document document, out PostFootprintSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        selection = null;

        while (true)
        {
            var firstResult = PromptForEntity(document.Editor, UiStrings.CommandPostFootprintEdgePrompt);
            var firstOutcome = MapPromptOutcome(firstResult.Status);
            if (TimberPostFootprintSelectionRules.Evaluate(firstOutcome, isLightweightPolyline: false) ==
                TimberPostFootprintSelectionDecision.Stop)
            {
                return false;
            }

            using var transaction = document.Database.TransactionManager.StartTransaction();
            var selectedEntity = transaction.GetObject(firstResult.ObjectId, OpenMode.ForRead) as Entity;
            if (TimberPostFootprintSelectionRules.Evaluate(
                    firstOutcome,
                    selectedEntity is Polyline) == TimberPostFootprintSelectionDecision.RejectEntity)
            {
                document.Editor.WriteMessage(UiStrings.CommandPostFootprintPolylineOnly);
                continue;
            }

            var polyline = (Polyline)selectedEntity!;
            if (!PostFootprintGeometryExtractor.TryExtract(polyline, out var geometry, out _) ||
                geometry is null)
            {
                document.Editor.WriteMessage(UiStrings.CommandPostFootprintInvalidGeometry);
                continue;
            }

            var edgePick = TimberPolylineSegmentPickResolver.Resolve(
                geometry,
                new TimberRectangularFootprintPoint(firstResult.PickedPoint.X, firstResult.PickedPoint.Y));
            if (!TryReadEdgeIndex(document.Editor, edgePick, out var widthEdgeIndex))
            {
                continue;
            }

            TimberRectangularFootprintDimensions dimensions;
            try
            {
                dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
                    geometry,
                    widthEdgeIndex);
            }
            catch (ArgumentException)
            {
                document.Editor.WriteMessage(UiStrings.CommandPostFootprintInvalidGeometry);
                continue;
            }

            transaction.Commit();
            selection = new PostFootprintSelection(firstResult.ObjectId, geometry, dimensions);
            return true;
        }
    }

    private static PromptEntityResult PromptForEntity(Editor editor, string message)
    {
        var options = new PromptEntityOptions(message);
        return editor.GetEntity(options);
    }

    private static TimberPostFootprintPromptOutcome MapPromptOutcome(PromptStatus status) => status switch
    {
        PromptStatus.OK => TimberPostFootprintPromptOutcome.Selected,
        PromptStatus.Cancel => TimberPostFootprintPromptOutcome.Cancelled,
        PromptStatus.None => TimberPostFootprintPromptOutcome.None,
        _ => TimberPostFootprintPromptOutcome.Error,
    };

    private static bool TryReadEdgeIndex(
        Editor editor,
        TimberPolylineSegmentPickResult result,
        out int edgeIndex)
    {
        edgeIndex = -1;
        if (result.Status == TimberPolylineSegmentPickStatus.Success && result.EdgeIndex is { } resolved)
        {
            edgeIndex = resolved;
            return true;
        }

        editor.WriteMessage(result.Status == TimberPolylineSegmentPickStatus.Ambiguous
            ? UiStrings.CommandPostFootprintAmbiguousPick
            : UiStrings.CommandPostFootprintPickTooFar);
        return false;
    }
}
