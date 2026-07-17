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
    private const double MinimumOffsetMm = 180d;
    private const double MaximumOffsetMm = 600d;

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

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(sourceEntity) || string.IsNullOrWhiteSpace(data.ElementId))
        {
            return false;
        }

        var measurement = TimberCalculator.Measure(data, AutoCadEntityHelpers.GetPlanLengthMm(sourceEntity));
        var placement = CalculatePlacement(sourceEntity, measurement.PlanLengthMm);
        var labelText = CreateContents(data, measurement);
        var sourceHandle = sourceEntity.Handle.ToString();
        var existingLabelId = FindExistingLabelId(database, transaction, data.ElementId, sourceHandle);
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

        foreach (var id in ids.Distinct())
        {
            try
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    skipped++;
                    continue;
                }

                if (UpsertForElement(database, transaction, entity, data))
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

    private static ObjectId FindExistingLabelId(
        Database database,
        Transaction transaction,
        string elementId,
        string sourceHandle)
    {
        var candidates = new List<(ObjectId Id, ElementLabelData Data)>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not MText text ||
                !ElementLabelStore.TryRead(text, out var data) ||
                data is null ||
                !string.Equals(data.ElementId, elementId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add((id, data));
        }

        var exact = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Data.SourceHandle, sourceHandle, StringComparison.OrdinalIgnoreCase));
        if (!exact.Id.IsNull)
        {
            return exact.Id;
        }

        // WBLOCK/COPY prenáša XData so starým Handle. Ak je v novom DWG len
        // jeden štítok s daným ElementId, bezpečne ho znovu pripájame k novému objektu.
        return candidates.Count == 1 ? candidates[0].Id : ObjectId.Null;
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

    private static LabelPlacement CalculatePlacement(Entity sourceEntity, double planLengthMm)
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

        var chord = end - start;
        var planarLength = Math.Sqrt(chord.X * chord.X + chord.Y * chord.Y);
        var rotation = planarLength < 0.001d ? 0d : Math.Atan2(chord.Y, chord.X);

        // Text sa nikdy nenechá otočiť hlavou nadol.
        if (rotation > Math.PI / 2d || rotation <= -Math.PI / 2d)
        {
            rotation += Math.PI;
        }

        var offset = Math.Clamp(planLengthMm * 0.03d, MinimumOffsetMm, MaximumOffsetMm);
        var normal = new Vector3d(-Math.Sin(rotation), Math.Cos(rotation), 0d);
        var location = midpoint + normal * offset;

        return new LabelPlacement(location, rotation);
    }

    private static string CreateContents(TimberElementData data, TimberElementMeasurement measurement) =>
        $"{data.ElementId}\\P{data.WidthMm:0} × {data.HeightMm:0}\\P{measurement.CuttingLengthMm:0} mm";

    private sealed record LabelPlacement(Point3d Location, double RotationRadians);
}

internal sealed record ElementLabelUpdateResult(int Created, int Updated, int Skipped)
{
    public int Processed => Created + Updated;
}
