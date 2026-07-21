using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class PostFootprintPerpendicularAnnotationService
{
    private const string BlockName = "DECORAIR_ACADKROVY_POST_FOOTPRINT_90_V2";
    private const int AnnotationLayerColorIndex = 8;

    public static bool UpsertForFootprint(
        Database database,
        Transaction transaction,
        Polyline sourcePolyline,
        TimberRectangularFootprintGeometry footprintGeometry)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourcePolyline);
        ArgumentNullException.ThrowIfNull(footprintGeometry);

        var sourceHandle = sourcePolyline.Handle.ToString();
        var blockId = EnsureBlockDefinition(database, transaction);
        var matching = ReadAnnotations(database, transaction)
            .Where(item => string.Equals(
                item.Data.SourceHandle,
                sourceHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selected = matching.FirstOrDefault(item =>
            item.Entity is BlockReference reference && reference.BlockTableRecord == blockId);
        var isCreated = selected is null;

        BlockReference annotation;
        if (isCreated)
        {
            annotation = new BlockReference(Point3d.Origin, blockId);
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(annotation);
            transaction.AddNewlyCreatedDBObject(annotation, true);
        }
        else
        {
            annotation = (BlockReference)transaction.GetObject(selected!.Id, OpenMode.ForWrite);
        }

        var placement = TimberPostFootprintPerpendicularGeometryCalculator.CalculatePlacement(
            footprintGeometry.Bounds);
        annotation.Position = new Point3d(
            placement.AnchorX,
            placement.AnchorY,
            sourcePolyline.GetPoint3dAt(0).Z);
        annotation.Rotation = placement.RotationRadians;
        annotation.ScaleFactors = new Scale3d(1d);
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            annotation,
            SlopeArrowService.ArrowLayerName,
            AnnotationLayerColorIndex,
            isPlottable: false);
        annotation.LineWeight = LineWeight.ByLayer;
        PostFootprintPerpendicularAnnotationStore.Write(
            annotation,
            transaction,
            new PostFootprintPerpendicularAnnotationData { SourceHandle = sourceHandle });

        DeleteAnnotations(
            transaction,
            matching.Where(item => item.Id != annotation.ObjectId).Select(item => item.Id));
        DeleteDuplicatesForExistingSourceHandles(database, transaction);
        return isCreated;
    }

    public static int DeleteForSourceHandle(
        Database database,
        Transaction transaction,
        string sourceHandle) =>
        DeleteAnnotations(
            transaction,
            ReadAnnotations(database, transaction)
                .Where(item => string.Equals(
                    item.Data.SourceHandle,
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Id));

    public static int DeleteForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        var targets = new HashSet<string>(sourceHandles, StringComparer.OrdinalIgnoreCase);
        var annotations = ReadAnnotations(database, transaction)
            .Where(item => targets.Contains(item.Data.SourceHandle))
            .ToList();
        return DeleteSelectedByCleanupRules(
            transaction,
            annotations,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    public static int DeleteInsertedWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> annotationIds)
    {
        var annotations = ReadAnnotations(database, transaction)
            .Where(item => annotationIds.Contains(item.Id))
            .ToList();
        return DeleteSelectedByCleanupRules(
            transaction,
            annotations,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    public static int DeleteDuplicatesForExistingSourceHandles(
        Database database,
        Transaction transaction) =>
        DeleteSelectedByCleanupRules(
            transaction,
            ReadAnnotations(database, transaction),
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: true);

    private static ObjectId EnsureBlockDefinition(Database database, Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        if (blockTable.Has(BlockName))
        {
            return blockTable[BlockName];
        }

        blockTable.UpgradeOpen();
        var definition = new BlockTableRecord { Name = BlockName, Origin = Point3d.Origin };
        blockTable.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);

        var local = TimberPostFootprintPerpendicularGeometryCalculator.CreateLocal();
        AddLine(database, transaction, definition, local.CapStart, local.CapEnd);
        AddLine(database, transaction, definition, local.StemStart, local.StemEnd);
        AddText(database, transaction, definition, local.TextPosition, local.Text);
        return definition.ObjectId;
    }

    private static void AddLine(
        Database database,
        Transaction transaction,
        BlockTableRecord definition,
        TimberRectangularFootprintPoint start,
        TimberRectangularFootprintPoint end)
    {
        var line = new Line(
            new Point3d(start.X, start.Y, 0d),
            new Point3d(end.X, end.Y, 0d));
        line.SetDatabaseDefaults(database);
        line.Layer = "0";
        line.ColorIndex = 0;
        line.LineWeight = LineWeight.ByBlock;
        definition.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
    }

    private static void AddText(
        Database database,
        Transaction transaction,
        BlockTableRecord definition,
        TimberRectangularFootprintPoint position,
        string textValue)
    {
        var position3d = new Point3d(position.X, position.Y, 0d);
        var text = new DBText();
        text.SetDatabaseDefaults(database);
        text.Height = TimberPostFootprintPerpendicularGeometryCalculator.TextHeightMm;
        text.TextString = textValue;
        text.Justify = AttachmentPoint.MiddleLeft;
        text.Position = position3d;
        text.AlignmentPoint = position3d;
        text.Rotation = 0d;
        text.Layer = "0";
        text.ColorIndex = 0;
        text.LineWeight = LineWeight.ByBlock;
        definition.AppendEntity(text);
        transaction.AddNewlyCreatedDBObject(text, true);
        text.AdjustAlignment(database);
    }

    private static IReadOnlyList<AnnotationEntry> ReadAnnotations(
        Database database,
        Transaction transaction)
    {
        var result = new List<AnnotationEntry>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            result.Add(new AnnotationEntry(id, entity, data));
        }

        return result;
    }

    private static IReadOnlySet<string> ReadTimberSourceHandles(
        Database database,
        Transaction transaction)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) &&
                entity is not null)
            {
                handles.Add(entity.Handle.ToString());
            }
        }

        return handles;
    }

    private static int DeleteAnnotations(Transaction transaction, IEnumerable<ObjectId> ids)
    {
        var deleted = 0;
        foreach (var id in ids.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var annotation) ||
                annotation is null ||
                !PostFootprintPerpendicularAnnotationStore.TryRead(annotation, out _))
            {
                continue;
            }

            annotation.Erase();
            deleted++;
        }

        return deleted;
    }

    private static int DeleteSelectedByCleanupRules(
        Transaction transaction,
        IReadOnlyList<AnnotationEntry> annotations,
        IReadOnlyCollection<string> existingTimberSourceHandles,
        bool deleteDuplicates)
    {
        var idsByKey = annotations.ToDictionary(item => item.Id.ToString(), item => item.Id);
        var candidates = annotations.Select(item => new TimberElementLabelCandidate
        {
            LabelKey = item.Id.ToString(),
            SourceHandle = item.Data.SourceHandle,
        }).ToList();
        var keys = deleteDuplicates
            ? TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
                candidates,
                existingTimberSourceHandles)
            : TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
                candidates,
                existingTimberSourceHandles);
        return DeleteAnnotations(
            transaction,
            keys.Where(idsByKey.ContainsKey).Select(key => idsByKey[key]));
    }

    private sealed record AnnotationEntry(
        ObjectId Id,
        Entity Entity,
        PostFootprintPerpendicularAnnotationData Data);
}
