using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.Colors;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Vytvára a aktualizuje hladiny ACAD KROVY a nastaví prvok na ByLayer farbu.
/// Hladiny sú súčasťou DWG, preto ich vzhľad ostáva zachovaný pri odovzdaní výkresu.
/// </summary>
internal static class TimberLayerService
{
    public static void ApplyToEntity(
        Database database,
        Transaction transaction,
        Entity entity,
        TimberElementType elementType,
        ElementLayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(profile);

        var style = profile.GetStyle(elementType);
        if (!LayerNameValidator.TryValidate(style.LayerName, out var layerName, out var error))
        {
            throw new InvalidOperationException($"Neplatná hladina pre prvok {elementType}: {error}");
        }

        var layerId = EnsureLayer(database, transaction, layerName, style.ColorIndex);
        entity.LayerId = layerId;
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
    }

    public static void ApplyToAnnotationEntity(
        Database database,
        Transaction transaction,
        Entity entity,
        string layerName,
        int colorIndex,
        bool isPlottable = true)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entity);

        if (!LayerNameValidator.TryValidate(layerName, out var normalizedLayerName, out var error))
        {
            throw new InvalidOperationException($"Neplatná hladina anotácie: {error}");
        }

        if (colorIndex is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(colorIndex));
        }

        entity.LayerId = EnsureLayer(database, transaction, normalizedLayerName, colorIndex);
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        var layer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForWrite);
        layer.IsPlottable = isPlottable;
    }

    private static ObjectId EnsureLayer(
        Database database,
        Transaction transaction,
        string layerName,
        int colorIndex)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;

        if (layerTable.Has(layerName))
        {
            layer = (LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForWrite);
        }
        else
        {
            layerTable.UpgradeOpen();
            layer = new LayerTableRecord
            {
                Name = layerName,
            };

            layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, add: true);
        }

        layer.Color = AcColor.FromColorIndex(ColorMethod.ByAci, checked((short)colorIndex));
        return layer.ObjectId;
    }
}
