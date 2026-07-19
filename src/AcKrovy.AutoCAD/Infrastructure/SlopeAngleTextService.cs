using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeAngleTextService
{
    private const int AngleTextLayerColorIndex = 8;
    private const double AngleTextHeightMm = 120d;
    private const double AngleTextOffsetMm = 140d;

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
        var matchingTexts = ReadTexts(database, transaction)
            .Where(text => TimberSlopeAnnotationRules.HasSameSourceHandle(
                text.Data.SourceHandle,
                sourceHandle))
            .ToList();

        if (!TimberSlopeArrowCalculator.ShouldDisplay(data.SlopeDegrees))
        {
            DeleteTexts(transaction, matchingTexts.Select(text => text.Id));
            return false;
        }

        DBText angleText;
        var isCreated = matchingTexts.Count == 0;
        if (isCreated)
        {
            angleText = new DBText();
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(angleText);
            transaction.AddNewlyCreatedDBObject(angleText, true);
        }
        else
        {
            angleText = (DBText)transaction.GetObject(matchingTexts[0].Id, OpenMode.ForWrite);
        }

        ApplyAppearance(database, transaction, angleText, sourceEntity, data.SlopeDegrees);
        SlopeAngleTextStore.Write(
            angleText,
            transaction,
            new SlopeAngleTextData { SourceHandle = sourceHandle });
        DeleteTexts(transaction, matchingTexts.Skip(1).Select(text => text.Id));
        DeleteDuplicateTextsForExistingSourceHandles(database, transaction);
        return isCreated;
    }

    internal static int DeleteTextsForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        if (sourceHandles.Count == 0)
        {
            return 0;
        }

        var targetHandles = new HashSet<string>(sourceHandles, StringComparer.OrdinalIgnoreCase);
        var texts = ReadTexts(database, transaction)
            .Where(text => targetHandles.Contains(text.Data.SourceHandle))
            .ToList();
        return DeleteTextsSelectedByCleanupRules(
            transaction,
            texts,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    internal static int DeleteInsertedTextsWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> textIds)
    {
        if (textIds.Count == 0)
        {
            return 0;
        }

        var texts = ReadTexts(database, transaction)
            .Where(text => textIds.Contains(text.Id))
            .ToList();
        return DeleteTextsSelectedByCleanupRules(
            transaction,
            texts,
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: false);
    }

    internal static int DeleteDuplicateTextsForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        return DeleteTextsSelectedByCleanupRules(
            transaction,
            ReadTexts(database, transaction),
            ReadTimberSourceHandles(database, transaction),
            deleteDuplicates: true);
    }

    private static void ApplyAppearance(
        Database database,
        Transaction transaction,
        DBText angleText,
        Entity sourceEntity,
        double slopeDegrees)
    {
        var geometry = SlopeAnnotationGeometry.Calculate(sourceEntity);
        var placement = TimberElementLabelPlacementCalculator.Calculate(
            geometry.Start.X,
            geometry.Start.Y,
            geometry.End.X,
            geometry.End.Y,
            geometry.AnnotationPoint.X,
            geometry.AnnotationPoint.Y,
            AngleTextOffsetMm);
        var location = new Point3d(placement.X, placement.Y, geometry.AnnotationPoint.Z);

        angleText.Position = location;
        angleText.Justify = AttachmentPoint.MiddleCenter;
        angleText.AlignmentPoint = location;
        angleText.Height = AngleTextHeightMm;
        angleText.Rotation = placement.RotationRadians;
        angleText.TextString = TimberSlopeAngleFormatter.Format(slopeDegrees);
        TimberLayerService.ApplyToAnnotationEntity(
            database,
            transaction,
            angleText,
            SlopeArrowService.ArrowLayerName,
            AngleTextLayerColorIndex,
            isPlottable: false);
        angleText.LineWeight = LineWeight.ByLayer;
        angleText.AdjustAlignment(database);
    }

    private static IReadOnlyList<(ObjectId Id, SlopeAngleTextData Data)> ReadTexts(
        Database database,
        Transaction transaction)
    {
        var texts = new List<(ObjectId Id, SlopeAngleTextData Data)>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<DBText>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var text,
                    database) ||
                text is null ||
                !SlopeAngleTextStore.TryRead(text, out var data) ||
                data is null)
            {
                continue;
            }

            texts.Add((id, data));
        }

        return texts;
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

    private static int DeleteTexts(Transaction transaction, IEnumerable<ObjectId> ids)
    {
        var deleted = 0;
        foreach (var id in ids.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<DBText>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var text) ||
                text is null ||
                !SlopeAngleTextStore.TryRead(text, out _))
            {
                continue;
            }

            text.Erase();
            deleted++;
        }

        return deleted;
    }

    private static int DeleteTextsSelectedByCleanupRules(
        Transaction transaction,
        IReadOnlyList<(ObjectId Id, SlopeAngleTextData Data)> texts,
        IReadOnlyCollection<string> existingTimberSourceHandles,
        bool deleteDuplicates)
    {
        var idsByKey = texts.ToDictionary(text => text.Id.ToString(), text => text.Id);
        var candidates = texts
            .Select(text => new TimberElementLabelCandidate
            {
                LabelKey = text.Id.ToString(),
                SourceHandle = text.Data.SourceHandle,
            })
            .ToList();
        var keys = deleteDuplicates
            ? TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
                candidates,
                existingTimberSourceHandles)
            : TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
                candidates,
                existingTimberSourceHandles);

        return DeleteTexts(
            transaction,
            keys.Where(idsByKey.ContainsKey).Select(key => idsByKey[key]));
    }
}
