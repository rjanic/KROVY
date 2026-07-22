using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class PostFootprintLineConversionService
{
    public static PostFootprintLineConversionResult CreatePolyline(
        Database database,
        Transaction transaction,
        PostFootprintSelection selection)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.RequiresLineConversion || selection.OrderedSourceLineIds.Count != 4)
        {
            throw new ArgumentException("Four source LINE entities are required.", nameof(selection));
        }

        var template = transaction.GetObject(selection.SelectedEntityId, OpenMode.ForRead) as Line
            ?? throw new InvalidOperationException("The selected source LINE is no longer available.");
        var conversionPlan = ValidateAndCreatePlan(
            transaction,
            selection.OrderedSourceLineIds,
            template.Handle.ToString(),
            selection.Elevation);
        var currentGeometry = new TimberRectangularFootprintGeometry(conversionPlan.Vertices);

        var polyline = new Polyline(4)
        {
            Closed = true,
            Elevation = selection.Elevation,
            Normal = Vector3d.ZAxis,
            Layer = template.Layer,
            Color = template.Color,
            Linetype = template.Linetype,
            LinetypeScale = template.LinetypeScale,
            LineWeight = template.LineWeight,
            Transparency = template.Transparency,
        };
        for (var index = 0; index < currentGeometry.Vertices.Count; index++)
        {
            var vertex = conversionPlan.Vertices[index];
            polyline.AddVertexAt(index, new Point2d(vertex.X, vertex.Y), conversionPlan.Bulges[index], 0d, 0d);
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        modelSpace.AppendEntity(polyline);
        transaction.AddNewlyCreatedDBObject(polyline, true);
        return new PostFootprintLineConversionResult(
            polyline,
            TimberRectangularFootprintEdgeRules.ResolveDimensions(
                currentGeometry,
                TimberLineRectangleDiscoveryResult.SelectedWidthEdgeIndex));
    }

    public static void EraseSourceLines(
        Transaction transaction,
        IReadOnlyList<ObjectId> sourceLineIds)
    {
        foreach (var id in sourceLineIds)
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Line line)
            {
                throw new InvalidOperationException("A source LINE is no longer available.");
            }

            line.Erase();
        }
    }

    private static TimberLineRectangleConversionPlan ValidateAndCreatePlan(
        Transaction transaction,
        IReadOnlyList<ObjectId> ids,
        string selectedKey,
        double elevation)
    {
        if (ids.Distinct().Count() != 4)
        {
            throw new InvalidOperationException("The source rectangle changed before conversion.");
        }

        var edges = new List<TimberLineRectangleEdge>(4);
        foreach (var id in ids)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Line line ||
                Math.Abs(line.StartPoint.Z - elevation) > PostFootprintSelectionService.LineElevationToleranceMm ||
                Math.Abs(line.EndPoint.Z - elevation) > PostFootprintSelectionService.LineElevationToleranceMm)
            {
                throw new InvalidOperationException("The source rectangle changed before conversion.");
            }

            edges.Add(new TimberLineRectangleEdge(
                line.Handle.ToString(),
                new TimberRectangularFootprintPoint(line.StartPoint.X, line.StartPoint.Y),
                new TimberRectangularFootprintPoint(line.EndPoint.X, line.EndPoint.Y)));
        }

        var discovery = TimberLineRectangleDiscoveryService.Discover(selectedKey, edges);
        return discovery.Status == TimberLineRectangleDiscoveryStatus.Success && discovery.Geometry is not null
            ? TimberLineRectangleConversionPlan.FromDiscovery(discovery)
            : throw new InvalidOperationException("The source rectangle changed before conversion.");
    }
}

internal sealed record PostFootprintLineConversionResult(
    Polyline Polyline,
    TimberRectangularFootprintDimensions Dimensions);
