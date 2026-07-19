using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Vytvára a obnovuje automatické MText štítky krovu. Popis je samostatný
/// objekt na hladine KROV_POPIS, ale obsahuje XData väzbu na ElementId prvku.
/// Pri WBLOCK je preto potrebné vybrať aj popisy; keď sa prenesie iba krov,
/// príkaz AK_LABELS popisy v cieľovom DWG bezpečne dopočíta znovu.
/// </summary>
internal static class ElementLabelService
{
    public const string LabelLayerName = "KROV_POPIS";

    private const short LabelLayerColorIndex = 8;
    private const double DefaultTextHeightMm = 180d;
    private const double LabelOffsetMm = 180d;

    public static bool UpsertForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity) || string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(data, AutoCadEntityHelpers.GetPlanLengthMm(sourceEntity), roundingStepMm);
        var labelText = TimberElementLabelFormatter.Format(data, measurement);
        var placement = CalculatePlacement(sourceEntity);
        var sourceHandle = sourceEntity.Handle.ToString();
        var existingLabelId = FindExistingLabelId(
            database,
            transaction,
            data.ElementId,
            sourceHandle,
            previousElementId,
            out var obsoleteLabelIds);
        var isCreated = existingLabelId.IsNull;

        MText label;
        if (isCreated)
        {
            label = new MText();
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);
            modelSpace.AppendEntity(label);
            transaction.AddNewlyCreatedDBObject(label, true);
        }
        else
        {
            label = (MText)transaction.GetObject(existingLabelId, OpenMode.ForWrite);
        }

        ApplyLabelAppearance(database, transaction, label, placement, labelText);
        ElementLabelStore.Write(label, transaction, new ElementLabelData
        {
            ElementId = data.ElementId,
            SourceHandle = sourceHandle,
        });
        DeleteObsoleteLabels(transaction, obsoleteLabelIds, label.ObjectId);
        DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);

        return isCreated;
    }

    public static ElementLabelUpdateResult UpdateAll(Database database, Editor editor)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = Update(
            database,
            transaction,
            editor,
            DrawingScanner.FindAllTimberElements(database, transaction, metadataStore),
            metadataStore);
        transaction.Commit();
        return result;
    }

    public static ElementLabelUpdateResult UpdateSelected(
        Database database,
        Editor editor,
        IReadOnlyList<ObjectId> ids)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var result = Update(database, transaction, editor, ids, metadataStore);
        transaction.Commit();
        return result;
    }

    public static bool SetVisible(Database database, Transaction transaction, bool visible)
    {
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (!table.Has(LabelLayerName))
        {
            return false;
        }

        var layer = (LayerTableRecord)transaction.GetObject(table[LabelLayerName], OpenMode.ForWrite);
        layer.IsOff = !visible;
        return true;
    }

    internal static IReadOnlyList<TimberElementLabelCandidate> ReadLabelCandidates(
        Database database,
        Transaction transaction)
    {
        return ReadLabels(database, transaction)
            .Select(label => new TimberElementLabelCandidate
            {
                LabelKey = label.Id.ToString(),
                ElementId = label.Data.ElementId,
                SourceHandle = label.Data.SourceHandle,
            })
            .ToList();
    }

    internal static bool TryGetLongitudinalInterval(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        out TimberSlopeAnnotationLongitudinalInterval interval)
    {
        interval = new TimberSlopeAnnotationLongitudinalInterval(0d, 0d);
        var sourceHandle = sourceEntity.Handle.ToString();
        var matchingLabel = ReadLabels(database, transaction)
            .FirstOrDefault(label => TimberSlopeAnnotationRules.HasSameSourceHandle(
                label.Data.SourceHandle,
                sourceHandle));
        if (matchingLabel == default ||
            !AutoCadObjectIdAccess.TryGetObject<MText>(
                transaction,
                matchingLabel.Id,
                OpenMode.ForRead,
                out var label,
                database) ||
            label is null)
        {
            return false;
        }

        var (start, end) = sourceEntity switch
        {
            Line line => (line.StartPoint, line.EndPoint),
            Polyline polyline => (polyline.StartPoint, polyline.EndPoint),
            _ => (Point3d.Origin, Point3d.Origin),
        };
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var axisLength = Math.Sqrt(dx * dx + dy * dy);
        if (axisLength < 0.001d)
        {
            return false;
        }

        var axisX = dx / axisLength;
        var axisY = dy / axisLength;
        var centerDistance = (label.Location.X - start.X) * axisX +
            (label.Location.Y - start.Y) * axisY;
        var halfWidth = label.ActualWidth / 2d;
        if (double.IsNaN(halfWidth) || double.IsInfinity(halfWidth) || halfWidth <= 0d)
        {
            return false;
        }

        interval = new TimberSlopeAnnotationLongitudinalInterval(
            centerDistance - halfWidth,
            centerDistance + halfWidth);
        return true;
    }

    internal static int DeleteLabelsForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        if (sourceHandles.Count == 0)
        {
            return 0;
        }

        var targetHandles = new HashSet<string>(
            sourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (targetHandles.Count == 0)
        {
            return 0;
        }

        var existingSourceHandles = ReadTimberSourceHandles(database, transaction);
        var deleted = 0;

        foreach (var label in ReadLabels(database, transaction))
        {
            if (!targetHandles.Contains(label.Data.SourceHandle) ||
                existingSourceHandles.Contains(label.Data.SourceHandle) ||
                transaction.GetObject(label.Id, OpenMode.ForWrite, false) is not MText text)
            {
                continue;
            }

            text.Erase();
            deleted++;
        }

        return deleted;
    }

    internal static int DeleteDuplicateLabelsForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        var labels = ReadLabels(database, transaction);
        if (labels.Count == 0)
        {
            return 0;
        }

        var labelIdsByKey = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var keysToDelete = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            labels
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                })
                .ToList(),
            ReadTimberSourceHandles(database, transaction));

        return DeleteLabelsByKey(transaction, labelIdsByKey, keysToDelete);
    }

    internal static int DeleteInsertedLabelsWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> labelIds)
    {
        if (labelIds.Count == 0)
        {
            return 0;
        }

        var labels = ReadLabels(database, transaction)
            .Where(label => labelIds.Contains(label.Id))
            .ToList();
        if (labels.Count == 0)
        {
            return 0;
        }

        var labelIdsByKey = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var keysToDelete = TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
            labels
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                })
                .ToList(),
            ReadTimberSourceHandles(database, transaction));

        return DeleteLabelsByKey(transaction, labelIdsByKey, keysToDelete);
    }

    private static ElementLabelUpdateResult Update(
        Database database,
        Transaction transaction,
        Editor editor,
        IReadOnlyList<ObjectId> ids,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var distinctIds = ids.Distinct().ToList();
        TimberElementCopyInitializationService.InitializeLocalCopies(
            database,
            transaction,
            metadataStore,
            distinctIds,
            defaultProfile);
        var previousElementIdById = ReadElementIds(transaction, metadataStore, distinctIds);
        var synchronizedDataById = TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            distinctIds,
            roundingStepMm);

        foreach (var id in distinctIds)
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !synchronizedDataById.TryGetValue(id, out var data))
                {
                    skipped++;
                    continue;
                }

                previousElementIdById.TryGetValue(id, out var previousElementId);
                if (TimberAnnotationService.EnsureForElement(
                        database,
                        transaction,
                        entity,
                        data,
                        previousElementId,
                        roundingStepMm))
                {
                    created++;
                }
                else
                {
                    updated++;
                }

            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage($"\nPopis nebol vytvorený/obnovený pre prvok {id}: {ex.Message}");
            }
        }

        TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(database, transaction);
        return new ElementLabelUpdateResult(created, updated, skipped);
    }

    private static IReadOnlyDictionary<ObjectId, string> ReadElementIds(
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<ObjectId> ids)
    {
        var result = new Dictionary<ObjectId, string>();

        foreach (var id in ids)
        {
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity) &&
                entity is not null &&
                metadataStore.TryRead(entity, out var data) &&
                data is not null)
            {
                result[id] = data.ElementId;
            }
        }

        return result;
    }

    private static ObjectId FindExistingLabelId(
        Database database,
        Transaction transaction,
        string elementId,
        string sourceHandle,
        string? previousElementId,
        out IReadOnlyList<ObjectId> obsoleteLabelIds)
    {
        obsoleteLabelIds = Array.Empty<ObjectId>();
        var labels = ReadLabels(database, transaction);

        var labelKeys = labels.ToDictionary(label => label.Id.ToString(), label => label.Id);
        var selection = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle,
            elementId,
            previousElementId,
            labels
                .Select(label => new TimberElementLabelCandidate
                {
                    LabelKey = label.Id.ToString(),
                    ElementId = label.Data.ElementId,
                    SourceHandle = label.Data.SourceHandle,
                })
                .ToList(),
            CountTimberElementsWithElementId(database, transaction, elementId),
            CountTimberElementsWithElementId(database, transaction, previousElementId));

        obsoleteLabelIds = selection.LabelKeysToDelete
            .Where(labelKeys.ContainsKey)
            .Select(labelKey => labelKeys[labelKey])
            .ToList();

        return selection.LabelKeyToUpdate is not null && labelKeys.TryGetValue(selection.LabelKeyToUpdate, out var labelId)
            ? labelId
            : ObjectId.Null;
    }

    private static IReadOnlyList<(ObjectId Id, ElementLabelData Data)> ReadLabels(
        Database database,
        Transaction transaction)
    {
        var labels = new List<(ObjectId Id, ElementLabelData Data)>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<MText>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var text,
                    database) ||
                text is null ||
                !ElementLabelStore.TryRead(text, out var data) ||
                data is null)
            {
                continue;
            }

            labels.Add((id, data));
        }

        return labels;
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

    private static void DeleteObsoleteLabels(
        Transaction transaction,
        IReadOnlyList<ObjectId> obsoleteLabelIds,
        ObjectId selectedLabelId)
    {
        foreach (var id in obsoleteLabelIds.Distinct())
        {
            if (id == selectedLabelId ||
                !AutoCadObjectIdAccess.TryGetObject<MText>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var label) ||
                label is null ||
                !ElementLabelStore.TryRead(label, out _))
            {
                continue;
            }

            label.Erase();
        }
    }

    private static int DeleteLabelsByKey(
        Transaction transaction,
        IReadOnlyDictionary<string, ObjectId> labelIdsByKey,
        IReadOnlyList<string> labelKeysToDelete)
    {
        var deleted = 0;

        foreach (var labelKey in labelKeysToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!labelIdsByKey.TryGetValue(labelKey, out var id) ||
                !AutoCadObjectIdAccess.TryGetObject<MText>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var label) ||
                label is null ||
                !ElementLabelStore.TryRead(label, out _))
            {
                continue;
            }

            label.Erase();
            deleted++;
        }

        return deleted;
    }

    private static int CountTimberElementsWithElementId(
        Database database,
        Transaction transaction,
        string? elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return 0;
        }

        var count = 0;
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null ||
                !string.Equals(data.ElementId, elementId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static void ApplyLabelAppearance(
        Database database,
        Transaction transaction,
        MText label,
        LabelPlacement placement,
        string contents)
    {
        var labelLayerId = EnsureLabelLayer(database, transaction);
        label.LayerId = labelLayerId;
        label.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        label.Attachment = AttachmentPoint.MiddleCenter;
        label.TextHeight = DefaultTextHeightMm;
        label.Rotation = placement.RotationRadians;
        label.Location = placement.Location;
        label.Contents = contents;
    }

    private static ObjectId EnsureLabelLayer(Database database, Transaction transaction)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;

        if (layerTable.Has(LabelLayerName))
        {
            layer = (LayerTableRecord)transaction.GetObject(layerTable[LabelLayerName], OpenMode.ForWrite);
        }
        else
        {
            layerTable.UpgradeOpen();
            layer = new LayerTableRecord { Name = LabelLayerName };
            layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
        }

        layer.Color = AcColor.FromColorIndex(ColorMethod.ByAci, LabelLayerColorIndex);
        return layer.ObjectId;
    }

    private static LabelPlacement CalculatePlacement(Entity sourceEntity)
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
            _ => throw new NotSupportedException("Popis možno vytvoriť iba pre LINE alebo LWPOLYLINE."),
        };

        var placement = TimberElementLabelPlacementCalculator.Calculate(
            start.X,
            start.Y,
            end.X,
            end.Y,
            midpoint.X,
            midpoint.Y,
            LabelOffsetMm);
        var location = new Point3d(placement.X, placement.Y, midpoint.Z);

        return new LabelPlacement(location, placement.RotationRadians);
    }

    private sealed record LabelPlacement(Point3d Location, double RotationRadians);
}

internal sealed record ElementLabelUpdateResult(int Created, int Updated, int Skipped)
{
    public int Processed => Created + Updated;
}
