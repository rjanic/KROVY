using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Read-only discovery and atomic rebuild operations for disposable roof display lines.</summary>
internal static class RoofDisplayService
{
    internal const string LayerName = "KROV_STRECHA";
    internal const int LayerColorIndex = 1;
    internal const string RidgeLayerName = "KROV_STRECHA_HREBEN";
    internal const int RidgeLayerColorIndex = 1;

    public static RoofDisplayInspection Inspect(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string generationSignature)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        var observations = new List<RoofDisplayObservation>();
        var childIds = new List<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) || entity is null)
            {
                continue;
            }

            var stored = RoofDisplayStore.Read(entity);
            if (!stored.Exists || !string.Equals(
                    stored.OwnerReference,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            childIds.Add(id);
            var segment = entity is Line line
                ? new RoofSegment3D(MapPoint(line.StartPoint), MapPoint(line.EndPoint))
                : new RoofSegment3D(
                    new RoofPoint3D(double.NaN, double.NaN, double.NaN),
                    new RoofPoint3D(double.NaN, double.NaN, double.NaN));
            observations.Add(new RoofDisplayObservation(
                stored.OwnerReference,
                stored.Data,
                stored.Error,
                segment,
                entity is Line));
        }

        var validation = RoofDisplayValidator.Validate(
            ownerReference,
            expectedEdges,
            generationSignature,
            observations);
        var group = RoofDisplayGroupService.Inspect(
            database,
            transaction,
            ownerId,
            childIds);
        return new RoofDisplayInspection(validation, group, childIds);
    }

    public static bool Rebuild(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlyList<RoofDisplayEdge> expectedEdges,
        string generationSignature)
    {
        var inspection = Inspect(
            database,
            transaction,
            ownerId,
            ownerReference,
            expectedEdges,
            generationSignature);
        if (inspection.Validation.IsCurrent)
        {
            foreach (var childId in inspection.ChildIds)
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        childId,
                        OpenMode.ForWrite,
                        out var currentLine,
                        database) || currentLine is null)
                {
                    return false;
                }
                var currentData = RoofDisplayStore.Read(currentLine).Data;
                if (currentData is null)
                {
                    return false;
                }
                ApplyDisplayLayer(database, transaction, currentLine, currentData.Role);
            }
            RoofDisplayGroupService.EnsureGroup(
                database,
                transaction,
                ownerId,
                inspection.ChildIds);
            return true;
        }
        if (inspection.Validation.Issues.HasFlag(
                RoofDisplayValidationIssue.UnsupportedFutureSchema))
        {
            return false;
        }

        foreach (var childId in inspection.ChildIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    childId,
                    OpenMode.ForWrite,
                    out var child,
                    database) || child is null)
            {
                continue;
            }

            var stored = RoofDisplayStore.Read(child);
            if (stored.Exists && string.Equals(
                    stored.OwnerReference,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                child.Erase();
            }
        }

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var newChildIds = new List<ObjectId>(SimpleGableRoofWireframe.EdgeCount);
        foreach (var edge in expectedEdges.OrderBy(edge => edge.Role))
        {
            var line = new Line(MapPoint(edge.Segment.Start), MapPoint(edge.Segment.End));
            line.SetDatabaseDefaults(database);
            ApplyDisplayLayer(database, transaction, line, edge.Role);
            var childId = modelSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
            newChildIds.Add(childId);
            RoofDisplayStore.Write(
                line,
                transaction,
                new RoofDisplayData(
                    RoofDisplayDataSchema.CurrentVersion,
                    ownerReference,
                    edge.Role,
                    generationSignature));
        }

        RoofDisplayGroupService.EnsureGroup(
            database,
            transaction,
            ownerId,
            newChildIds);

        return true;
    }

    private static RoofPoint3D MapPoint(Point3d point) =>
        new(point.X, point.Y, point.Z);

    private static Point3d MapPoint(RoofPoint3D point) =>
        new(point.X, point.Y, point.Z);

    private static void ApplyDisplayLayer(
        Database database,
        Transaction transaction,
        Line line,
        RoofDisplayEdgeRole role)
    {
        var isRidge = role == RoofDisplayEdgeRole.Ridge;
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            line,
            isRidge ? RidgeLayerName : LayerName,
            isRidge ? RidgeLayerColorIndex : LayerColorIndex);
        line.LinetypeId = database.ByLayerLinetype;
        line.LinetypeScale = 1d;
        line.LineWeight = LineWeight.ByLayer;
    }
}

internal sealed record RoofDisplayInspection(
    RoofDisplayValidationResult Validation,
    RoofDisplayGroupInspection Group,
    IReadOnlyList<ObjectId> ChildIds);
