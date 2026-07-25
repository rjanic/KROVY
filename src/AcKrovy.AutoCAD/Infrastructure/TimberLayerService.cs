using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Autodesk.AutoCAD.Colors;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Vytvára a aktualizuje hladiny ACAD KROVY a nastaví prvok na ByLayer farbu a typ čiary.
/// Hladiny sú súčasťou DWG, preto ich vzhľad ostáva zachovaný pri odovzdaní výkresu.
/// </summary>
internal static class TimberLayerService
{
    private const string StandardMetricLinetypeFile = "acadiso.lin";

    public static CadLayerApplyResult ApplyToEntity(
        Database database,
        Transaction transaction,
        Entity entity,
        TimberElementType elementType,
        ElementLayerProfile profile,
        CadLayerUpdateMode updateMode = CadLayerUpdateMode.PreserveExisting)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(profile);

        var style = profile.GetStyle(elementType);
        if (!LayerNameValidator.TryValidate(style.LayerName, out var layerName, out var error))
        {
            throw new InvalidOperationException(UiStrings.Format(
                UiStrings.ErrorInvalidElementLayerFormat,
                elementType,
                error));
        }

        var (linetypeId, linetypeResult, linetypeLoaded) = EnsureLinetype(
            database,
            transaction,
            style.LinetypeName);
        var layerResult = EnsureLayer(
            database,
            transaction,
            layerName,
            style.ColorIndex,
            linetypeId,
            updateMode);
        var entityChanged =
            entity.LayerId != layerResult.LayerId ||
            entity.ColorIndex != 256 ||
            entity.LinetypeId != database.ByLayerLinetype ||
            Math.Abs(entity.LinetypeScale - style.LinetypeScale) > 0.0000001;
        if (entityChanged)
        {
            if (!entity.IsWriteEnabled)
            {
                entity.UpgradeOpen();
            }

            entity.LayerId = layerResult.LayerId;
            entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
            entity.LinetypeId = database.ByLayerLinetype;
            entity.LinetypeScale = style.LinetypeScale;
        }

        return linetypeResult.WithApplication(
            layerResult.AppliedLinetypeName,
            layerResult.PreservedConflict ? layerName : null,
            linetypeLoaded || layerResult.Changed || entityChanged);
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
            throw new InvalidOperationException(UiStrings.Format(
                UiStrings.ErrorInvalidAnnotationLayerFormat,
                error));
        }

        if (colorIndex is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(colorIndex));
        }

        entity.LayerId = EnsureLayer(
            database,
            transaction,
            normalizedLayerName,
            colorIndex,
            ObjectId.Null,
            CadLayerUpdateMode.UpdateExisting).LayerId;
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        var layer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForWrite);
        layer.IsPlottable = isPlottable;
    }

    private static LayerEnsureResult EnsureLayer(
        Database database,
        Transaction transaction,
        string layerName,
        int colorIndex,
        ObjectId linetypeId,
        CadLayerUpdateMode updateMode)
    {
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer;
        var created = false;

        if (layerTable.Has(layerName))
        {
            layer = (LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForRead);
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
            created = true;
        }

        var requestedLinetypeName = linetypeId.IsNull
            ? CadLinetypeNames.Continuous
            : GetCanonicalLinetypeName(transaction, linetypeId);
        var existingLinetypeName = created || layer.LinetypeObjectId.IsNull
            ? requestedLinetypeName
            : GetCanonicalLinetypeName(transaction, layer.LinetypeObjectId);
        var appearanceDiffers = !linetypeId.IsNull &&
            CadLayerAppearanceRules.Differs(
                layer.Color.ColorIndex,
                existingLinetypeName,
                colorIndex,
                requestedLinetypeName);
        if (linetypeId.IsNull)
        {
            appearanceDiffers = layer.Color.ColorIndex != colorIndex;
        }
        if (!created &&
            appearanceDiffers &&
            !CadLayerAppearanceRules.ShouldUpdateExisting(updateMode, appearanceDiffers))
        {
            return new LayerEnsureResult(
                layer.ObjectId,
                existingLinetypeName,
                PreservedConflict: true,
                Changed: false);
        }

        var changed = created ||
            CadLayerAppearanceRules.ShouldUpdateExisting(updateMode, appearanceDiffers);
        if (changed)
        {
            if (!layer.IsWriteEnabled)
            {
                layer.UpgradeOpen();
            }

            layer.Color = AcColor.FromColorIndex(ColorMethod.ByAci, checked((short)colorIndex));
            if (!linetypeId.IsNull)
            {
                layer.LinetypeObjectId = linetypeId;
            }
        }

        return new LayerEnsureResult(
            layer.ObjectId,
            requestedLinetypeName,
            PreservedConflict: false,
            Changed: changed);
    }

    internal static IReadOnlyList<string> GetAvailableLinetypeNames(
        Database database,
        Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var table = (LinetypeTable)transaction.GetObject(
            database.LinetypeTableId,
            OpenMode.ForRead);
        var names = new HashSet<string>(
            CadLinetypeNames.SupportedStandardNames,
            StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in table)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is LinetypeTableRecord record)
            {
                names.Add(record.Name);
            }
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> GetConflictingExistingLayerNames(
        Database database,
        Transaction transaction,
        ElementLayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(profile);

        var layerTable = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in profile.Normalize().Styles)
        {
            if (!layerTable.Has(style.LayerName))
            {
                continue;
            }

            var layer = (LayerTableRecord)transaction.GetObject(
                layerTable[style.LayerName],
                OpenMode.ForRead);
            var currentLinetypeName = GetCanonicalLinetypeName(
                transaction,
                layer.LinetypeObjectId);
            if (layer.Color.ColorIndex != style.ColorIndex ||
                !string.Equals(
                    currentLinetypeName,
                    CadLinetypeNames.Normalize(style.LinetypeName),
                    StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(style.LayerName);
            }
        }

        return conflicts.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (ObjectId LinetypeId, CadLayerApplyResult Result, bool Loaded) EnsureLinetype(
        Database database,
        Transaction transaction,
        string? requestedName)
    {
        LinetypeTable GetTable() =>
            (LinetypeTable)transaction.GetObject(
                database.LinetypeTableId,
                OpenMode.ForRead);

        var requestedWasLoaded = GetTable().Has(CadLinetypeNames.Normalize(requestedName));
        var result = CadLinetypeResolutionRules.Resolve(
            requestedName,
            name => GetTable().Has(name),
            name =>
            {
                try
                {
                    database.LoadLineTypeFile(name, StandardMetricLinetypeFile);
                    return true;
                }
                catch (System.Exception)
                {
                    // Chýbajúci support súbor alebo definícia nesmie zablokovať ostatné layer nastavenia.
                    return false;
                }
            });
        var table = GetTable();
        var appliedId = table[result.AppliedLinetypeName];
        if (!result.UsedFallback)
        {
            var canonicalName = GetCanonicalLinetypeName(transaction, appliedId);
            result = CadLayerApplyResult.Applied(canonicalName);
        }

        return (appliedId, result, !requestedWasLoaded && !result.UsedFallback);
    }

    private static string GetCanonicalLinetypeName(
        Transaction transaction,
        ObjectId id) =>
        ((LinetypeTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name;

    private sealed record LayerEnsureResult(
        ObjectId LayerId,
        string AppliedLinetypeName,
        bool PreservedConflict,
        bool Changed);
}
