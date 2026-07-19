using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeArrowService
{
    public const string ArrowLayerName = "KROV_SKLON";

    private const int ArrowLayerColorIndex = 8;

    public static bool UpsertForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity))
        {
            return false;
        }

        var sourceHandle = sourceEntity.Handle.ToString();
        var matchingArrows = ReadArrows(database, transaction)
            .Where(arrow => string.Equals(
                arrow.Data.SourceHandle,
                sourceHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!TimberSlopeArrowCalculator.ShouldDisplay(data.SlopeDegrees))
        {
            DeleteArrows(transaction, matchingArrows.Select(arrow => arrow.Id));
            return false;
        }

        var placement = CalculatePlacement(sourceEntity, data.IsSlopeDirectionReversed);
        Polyline arrow;
        var isCreated = matchingArrows.Count == 0;

        if (isCreated)
        {
            arrow = new Polyline(5);
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(arrow);
            transaction.AddNewlyCreatedDBObject(arrow, true);
        }
        else
        {
            arrow = (Polyline)transaction.GetObject(matchingArrows[0].Id, OpenMode.ForWrite);
        }

        ApplyAppearance(database, transaction, arrow, placement.Geometry, placement.Elevation);
        SlopeArrowStore.Write(arrow, transaction, new SlopeArrowData { SourceHandle = sourceHandle });
        DeleteArrows(transaction, matchingArrows.Skip(1).Select(item => item.Id));
        DeleteDuplicateArrowsForExistingSourceHandles(database, transaction);
        return isCreated;
    }

    internal static int DeleteArrowsForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        if (sourceHandles.Count == 0)
        {
            return 0;
        }

        var targetHandles = new HashSet<string>(sourceHandles, StringComparer.OrdinalIgnoreCase);
        var existingHandles = ReadTimberSourceHandles(database, transaction);
        var arrows = ReadArrows(database, transaction)
            .Where(arrow => targetHandles.Contains(arrow.Data.SourceHandle))
            .ToList();
        return DeleteArrowsSelectedByCleanupRules(transaction, arrows, existingHandles, deleteDuplicates: false);
    }

    internal static int DeleteInsertedArrowsWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> arrowIds)
    {
        if (arrowIds.Count == 0)
        {
            return 0;
        }

        var arrows = ReadArrows(database, transaction)
            .Where(arrow => arrowIds.Contains(arrow.Id))
            .ToList();
        return DeleteArrowsSelectedByCleanupRules(
            transaction,
            arrows,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    internal static int DeleteDuplicateArrowsForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        var arrows = ReadArrows(database, transaction);
        var existingHandles = ReadTimberSourceHandles(database, transaction);
        return DeleteArrowsSelectedByCleanupRules(transaction, arrows, existingHandles, deleteDuplicates: true);
    }

    private static ArrowPlacement CalculatePlacement(Entity sourceEntity, bool isReversed)
    {
        var (start, end, midpoint) = sourceEntity switch
        {
            Line line => (
                line.StartPoint,
                line.EndPoint,
                new Point3d(
                    (line.StartPoint.X + line.EndPoint.X) / 2d,
                    (line.StartPoint.Y + line.EndPoint.Y) / 2d,
                    (line.StartPoint.Z + line.EndPoint.Z) / 2d)),
            Polyline polyline => (
                polyline.StartPoint,
                polyline.EndPoint,
                polyline.GetPointAtDist(polyline.Length / 2d)),
            _ => throw new NotSupportedException("Šípku sklonu možno vytvoriť iba pre LINE alebo LWPOLYLINE."),
        };

        return new ArrowPlacement(
            TimberSlopeArrowCalculator.Calculate(
                start.X,
                start.Y,
                end.X,
                end.Y,
                midpoint.X,
                midpoint.Y,
                isReversed),
            midpoint.Z);
    }

    private static void ApplyAppearance(
        Database database,
        Transaction transaction,
        Polyline arrow,
        TimberSlopeArrowPlacement placement,
        double elevation)
    {
        while (arrow.NumberOfVertices > 0)
        {
            arrow.RemoveVertexAt(arrow.NumberOfVertices - 1);
        }

        arrow.AddVertexAt(0, new Point2d(placement.TailX, placement.TailY), 0d, 0d, 0d);
        arrow.AddVertexAt(1, new Point2d(placement.TipX, placement.TipY), 0d, 0d, 0d);
        arrow.AddVertexAt(2, new Point2d(placement.HeadLeftX, placement.HeadLeftY), 0d, 0d, 0d);
        arrow.AddVertexAt(3, new Point2d(placement.TipX, placement.TipY), 0d, 0d, 0d);
        arrow.AddVertexAt(4, new Point2d(placement.HeadRightX, placement.HeadRightY), 0d, 0d, 0d);
        arrow.Elevation = elevation;
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            arrow,
            ArrowLayerName,
            ArrowLayerColorIndex);
        arrow.LineWeight = LineWeight.ByLayer;
    }

    private static IReadOnlyList<(ObjectId Id, SlopeArrowData Data)> ReadArrows(
        Database database,
        Transaction transaction)
    {
        var arrows = new List<(ObjectId Id, SlopeArrowData Data)>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var arrow,
                    database) ||
                arrow is null ||
                !SlopeArrowStore.TryRead(arrow, out var data) ||
                data is null)
            {
                continue;
            }

            arrows.Add((id, data));
        }

        return arrows;
    }

    private static IReadOnlySet<string> ReadTimberSourceHandles(Database database, Transaction transaction)
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

    private static int DeleteArrows(Transaction transaction, IEnumerable<ObjectId> ids)
    {
        var deleted = 0;
        foreach (var id in ids.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var arrow) ||
                arrow is null ||
                !SlopeArrowStore.TryRead(arrow, out _))
            {
                continue;
            }

            arrow.Erase();
            deleted++;
        }

        return deleted;
    }

    private static int DeleteArrowsSelectedByCleanupRules(
        Transaction transaction,
        IReadOnlyList<(ObjectId Id, SlopeArrowData Data)> arrows,
        IReadOnlyCollection<string> existingTimberSourceHandles,
        bool deleteDuplicates)
    {
        var idsByKey = arrows.ToDictionary(arrow => arrow.Id.ToString(), arrow => arrow.Id);
        var candidates = arrows
            .Select(arrow => new TimberElementLabelCandidate
            {
                LabelKey = arrow.Id.ToString(),
                SourceHandle = arrow.Data.SourceHandle,
            })
            .ToList();
        var keys = deleteDuplicates
            ? TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
                candidates,
                existingTimberSourceHandles)
            : TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
                candidates,
                existingTimberSourceHandles);

        return DeleteArrows(
            transaction,
            keys.Where(idsByKey.ContainsKey).Select(key => idsByKey[key]));
    }

    private sealed record ArrowPlacement(TimberSlopeArrowPlacement Geometry, double Elevation);
}
