using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum PostFootprintSelectionSourceKind
{
    ExistingPolyline,
    SeparateLines,
}

internal sealed record PostFootprintSelection(
    PostFootprintSelectionSourceKind SourceKind,
    ObjectId SelectedEntityId,
    IReadOnlyList<ObjectId> OrderedSourceLineIds,
    TimberRectangularFootprintGeometry Geometry,
    TimberRectangularFootprintDimensions Dimensions,
    double Elevation)
{
    public bool RequiresLineConversion => SourceKind == PostFootprintSelectionSourceKind.SeparateLines;
}

internal static class PostFootprintSelectionService
{
    internal const double LineElevationToleranceMm = 0.000001d;

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
            if (selectedEntity is Polyline polyline)
            {
                if (TryResolvePolylineSelection(
                        document.Editor,
                        firstResult,
                        polyline,
                        out selection))
                {
                    transaction.Commit();
                    return true;
                }

                continue;
            }

            if (selectedEntity is Line selectedLine)
            {
                if (TryResolveLineSelection(
                        document,
                        transaction,
                        selectedLine,
                        out selection))
                {
                    transaction.Commit();
                    return true;
                }

                continue;
            }

            document.Editor.WriteMessage(UiStrings.CommandPostFootprintPolylineOnly);
        }
    }

    private static bool TryResolvePolylineSelection(
        Editor editor,
        PromptEntityResult prompt,
        Polyline polyline,
        out PostFootprintSelection? selection)
    {
        selection = null;
        if (!PostFootprintGeometryExtractor.TryExtract(polyline, out var geometry, out _) ||
            geometry is null)
        {
            editor.WriteMessage(UiStrings.CommandPostFootprintInvalidGeometry);
            return false;
        }

        var edgePick = TimberPolylineSegmentPickResolver.Resolve(
            geometry,
            new TimberRectangularFootprintPoint(prompt.PickedPoint.X, prompt.PickedPoint.Y));
        if (!TryReadEdgeIndex(editor, edgePick, out var widthEdgeIndex))
        {
            return false;
        }

        TimberRectangularFootprintDimensions dimensions;
        try
        {
            dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(geometry, widthEdgeIndex);
        }
        catch (ArgumentException)
        {
            editor.WriteMessage(UiStrings.CommandPostFootprintInvalidGeometry);
            return false;
        }

        selection = new PostFootprintSelection(
            PostFootprintSelectionSourceKind.ExistingPolyline,
            prompt.ObjectId,
            Array.Empty<ObjectId>(),
            geometry,
            dimensions,
            polyline.Elevation);
        return true;
    }

    private static bool TryResolveLineSelection(
        Document document,
        Transaction transaction,
        Line selectedLine,
        out PostFootprintSelection? selection)
    {
        selection = null;
        if (!TryGetSupportedElevation(selectedLine, out var elevation))
        {
            document.Editor.WriteMessage(UiStrings.CommandPostFootprintUnsupportedPlane);
            return false;
        }

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (metadataStore.TryRead(selectedLine, out _))
        {
            document.Editor.WriteMessage(UiStrings.CommandPostFootprintPolylineOnly);
            return false;
        }

        var idsByKey = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var candidates = new List<TimberLineRectangleEdge>();
        var allPlanCandidates = new List<TimberLineRectangleEdge>();
        var compatiblePlaneKeys = new HashSet<string>(StringComparer.Ordinal);
        var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Line line ||
                metadataStore.TryRead(line, out _))
            {
                continue;
            }

            var key = line.Handle.ToString();
            idsByKey[key] = id;
            var candidate = new TimberLineRectangleEdge(
                key,
                new TimberRectangularFootprintPoint(line.StartPoint.X, line.StartPoint.Y),
                new TimberRectangularFootprintPoint(line.EndPoint.X, line.EndPoint.Y));
            allPlanCandidates.Add(candidate);
            if (IsAtElevation(line, elevation))
            {
                candidates.Add(candidate);
                compatiblePlaneKeys.Add(key);
            }
        }

        var selectedKey = selectedLine.Handle.ToString();
        var result = TimberLineRectangleDiscoveryService.Discover(selectedKey, candidates);
        if (result.Status != TimberLineRectangleDiscoveryStatus.Success || result.Geometry is null)
        {
            var planResult = TimberLineRectangleDiscoveryService.Discover(selectedKey, allPlanCandidates);
            if (planResult.Status == TimberLineRectangleDiscoveryStatus.Success &&
                planResult.OrderedEdgeKeys.Any(key => !compatiblePlaneKeys.Contains(key)))
            {
                document.Editor.WriteMessage(UiStrings.CommandPostFootprintUnsupportedPlane);
                return false;
            }

            document.Editor.WriteMessage(GetLineDiscoveryError(result.Status));
            return false;
        }

        var orderedIds = result.OrderedEdgeKeys.Select(key => idsByKey[key]).ToArray();
        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            result.Geometry,
            TimberLineRectangleDiscoveryResult.SelectedWidthEdgeIndex);
        selection = new PostFootprintSelection(
            PostFootprintSelectionSourceKind.SeparateLines,
            selectedLine.ObjectId,
            orderedIds,
            result.Geometry,
            dimensions,
            elevation);
        return true;
    }

    private static bool TryGetSupportedElevation(Line line, out double elevation)
    {
        elevation = line.StartPoint.Z;
        return Math.Abs(line.EndPoint.Z - elevation) <= LineElevationToleranceMm;
    }

    private static bool IsAtElevation(Line line, double elevation) =>
        Math.Abs(line.StartPoint.Z - elevation) <= LineElevationToleranceMm &&
        Math.Abs(line.EndPoint.Z - elevation) <= LineElevationToleranceMm;

    private static string GetLineDiscoveryError(TimberLineRectangleDiscoveryStatus status) => status switch
    {
        TimberLineRectangleDiscoveryStatus.Ambiguous => UiStrings.CommandPostFootprintLineAmbiguous,
        TimberLineRectangleDiscoveryStatus.Branching => UiStrings.CommandPostFootprintLineBranching,
        TimberLineRectangleDiscoveryStatus.InvalidRectangle => UiStrings.CommandPostFootprintLineNotRectangle,
        TimberLineRectangleDiscoveryStatus.DuplicateEdge => UiStrings.CommandPostFootprintLineDuplicate,
        _ => UiStrings.CommandPostFootprintLineNotFound,
    };

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
