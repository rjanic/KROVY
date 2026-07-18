using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
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
        string? previousElementId = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sourceEntity);
        ArgumentNullException.ThrowIfNull(data);

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity) || string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(data, AutoCadEntityHelpers.GetPlanLengthMm(sourceEntity));
        var placement = CalculatePlacement(sourceEntity);
        var labelText = CreateContents(data, measurement);
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
        var distinctIds = ids.Distinct().ToList();
        var previousElementIdById = ReadElementIds(transaction, metadataStore, distinctIds);
        var synchronizedDataById = TimberElementItemIdentityService.SynchronizeElementIds(
            database,
            transaction,
            metadataStore,
            distinctIds);

        foreach (var id in distinctIds)
        {
            try
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !synchronizedDataById.TryGetValue(id, out var data))
                {
                    skipped++;
                    continue;
                }

                previousElementIdById.TryGetValue(id, out var previousElementId);
                if (UpsertForElement(database, transaction, entity, data, previousElementId))
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
            if (transaction.GetObject(id, OpenMode.ForRead) is Entity entity &&
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
        var labels = new List<(ObjectId Id, ElementLabelData Data)>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not MText text ||
                !ElementLabelStore.TryRead(text, out var data) ||
                data is null)
            {
                continue;
            }

            labels.Add((id, data));
        }

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

    private static void DeleteObsoleteLabels(
        Transaction transaction,
        IReadOnlyList<ObjectId> obsoleteLabelIds,
        ObjectId selectedLabelId)
    {
        foreach (var id in obsoleteLabelIds.Distinct())
        {
            if (id == selectedLabelId ||
                transaction.GetObject(id, OpenMode.ForWrite, false) is not MText label ||
                !ElementLabelStore.TryRead(label, out _))
            {
                continue;
            }

            label.Erase();
        }
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
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
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

    private static string CreateContents(TimberElementData data, TimberElementMeasurement measurement) =>
        $"{data.ElementId}\\P{data.WidthMm:0} × {data.HeightMm:0}\\P{measurement.CuttingLengthMm:0} mm";

    private sealed record LabelPlacement(Point3d Location, double RotationRadians);
}

internal sealed record ElementLabelUpdateResult(int Created, int Updated, int Skipped)
{
    public int Processed => Created + Updated;
}
