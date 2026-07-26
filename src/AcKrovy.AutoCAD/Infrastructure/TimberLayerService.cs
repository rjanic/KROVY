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
        bool isPlottable = true,
        bool updateExistingLayer = true)
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
            updateExistingLayer
                ? CadLayerUpdateMode.UpdateExisting
                : CadLayerUpdateMode.PreserveExisting).LayerId;
        entity.Color = AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
        if (updateExistingLayer)
        {
            var layer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForWrite);
            layer.IsPlottable = isPlottable;
        }
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

    internal static IReadOnlyList<string> GetAvailableLayerNames(
        Database database,
        Transaction transaction)
    {
        var candidates = GetAvailableLayerPresets(database, transaction)
            .Select(preset => new CadLayerNameCandidate(preset.Name));
        return CadLayerNameRules.SelectUsableLocalNames(candidates);
    }

    internal static IReadOnlyList<CadLayerPreset> GetAvailableLayerPresets(
        Database database,
        Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);

        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var scalesByLayer = new Dictionary<string, List<double>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in DrawingScanner.FindAllTimberElements(
                     database,
                     transaction,
                     metadataStore))
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            if (!scalesByLayer.TryGetValue(entity.Layer, out var scales))
            {
                scales = [];
                scalesByLayer.Add(entity.Layer, scales);
            }

            scales.Add(entity.LinetypeScale);
        }

        var table = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        var presets = new List<CadLayerPreset>();
        foreach (ObjectId id in table)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not LayerTableRecord layer ||
                layer.IsErased ||
                layer.IsDependent)
            {
                continue;
            }

            scalesByLayer.TryGetValue(layer.Name, out var layerScales);
            var scaleResolution = CadLayerScaleHydrationRules.Resolve(
                currentProfileValue: 1d,
                layerScales ?? []);
            presets.Add(new CadLayerPreset(
                layer.Name,
                layer.Color.ColorIndex,
                GetCanonicalLinetypeName(transaction, layer.LinetypeObjectId),
                scaleResolution.LoadedFromEntities ? scaleResolution.Value : null,
                scaleResolution.HasMixedValues));
        }

        return presets
            .OrderBy(preset =>
                string.Equals(preset.Name, "0", StringComparison.OrdinalIgnoreCase) ? 0 :
                string.Equals(preset.Name, "Defpoints", StringComparison.OrdinalIgnoreCase) ? 1 :
                2)
            .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static NewOnlyLayerResolutionResult ResolveNewElementsOnlyProfile(
        Database database,
        Transaction transaction,
        ElementLayerProfile profile,
        ElementLayerProfile persistedProfile,
        IReadOnlyCollection<CadLayerOverrideIntent> overrideIntents)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(persistedProfile);
        ArgumentNullException.ThrowIfNull(overrideIntents);

        var normalized = profile.Normalize();
        var persistedScaleByLayer = persistedProfile.Normalize().Styles
            .GroupBy(style => style.LayerName, StringComparer.OrdinalIgnoreCase)
            .Where(group =>
            {
                var first = group.First().LinetypeScale;
                return group.All(style =>
                    Math.Abs(style.LinetypeScale - first) <=
                    CadLayerScaleHydrationRules.ComparisonTolerance);
            })
            .ToDictionary(
                group => group.Key,
                group => group.First().LinetypeScale,
                StringComparer.OrdinalIgnoreCase);
        var intentByType = overrideIntents
            .GroupBy(intent => intent.ElementType)
            .ToDictionary(group => group.Key, group => group.Last());
        var table = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        var occupied = GetAvailableLayerNames(database, transaction)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in table)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is LayerTableRecord layer)
            {
                occupied.Add(layer.Name);
            }
        }
        var presets = GetAvailableLayerPresets(database, transaction);

        var generatedByAppearance = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var createdNames = new List<string>();
        var resolvedStyles = new List<ElementLayerStyle>();
        foreach (var style in normalized.Styles)
        {
            if (!table.Has(style.LayerName))
            {
                var (linetypeId, _, _) = EnsureLinetype(
                    database,
                    transaction,
                    style.LinetypeName);
                _ = EnsureLayer(
                    database,
                    transaction,
                    style.LayerName,
                    style.ColorIndex,
                    linetypeId,
                    CadLayerUpdateMode.UpdateExisting);
                occupied.Add(style.LayerName);
                createdNames.Add(style.LayerName);
                resolvedStyles.Add(CloneStyle(style, style.LayerName));
                continue;
            }

            var existing = (LayerTableRecord)transaction.GetObject(
                table[style.LayerName],
                OpenMode.ForRead);
            intentByType.TryGetValue(style.ElementType, out var intent);
            var requiresSuffix = CadLayerOverrideRules.RequiresSuffix(
                style.LayerName,
                LayerMatches(existing, transaction, style),
                intent);
            if (!requiresSuffix)
            {
                resolvedStyles.Add(CloneStyle(style, style.LayerName));
                continue;
            }

            var canonicalBaseName = CadLayerNameRules.GetCanonicalBaseName(
                style.LayerName);
            var matchingPreset = presets
                .Where(preset => CadLayerNameRules.IsCanonicalOrGeneratedVariant(
                    preset.Name,
                    canonicalBaseName))
                .OrderBy(preset =>
                    string.Equals(
                        preset.Name,
                        canonicalBaseName,
                        StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(preset => LayerPresetMatches(
                    preset,
                    style,
                    persistedScaleByLayer));
            if (matchingPreset is not null)
            {
                resolvedStyles.Add(CloneStyle(style, matchingPreset.Name));
                continue;
            }

            var appearanceKey = string.Join(
                "\u001f",
                canonicalBaseName,
                style.ColorIndex,
                CadLinetypeNames.Normalize(style.LinetypeName),
                style.LinetypeScale);
            if (!generatedByAppearance.TryGetValue(appearanceKey, out var generatedName))
            {
                generatedName = CadLayerNameRules.NextConflictFreeName(
                    canonicalBaseName,
                    occupied);
                var (linetypeId, _, _) = EnsureLinetype(
                    database,
                    transaction,
                    style.LinetypeName);
                _ = EnsureLayer(
                    database,
                    transaction,
                    generatedName,
                    style.ColorIndex,
                    linetypeId,
                    CadLayerUpdateMode.UpdateExisting);
                generatedByAppearance.Add(appearanceKey, generatedName);
                occupied.Add(generatedName);
                createdNames.Add(generatedName);
            }

            resolvedStyles.Add(CloneStyle(style, generatedName));
        }

        return new NewOnlyLayerResolutionResult(
            new ElementLayerProfile { Styles = resolvedStyles }.Normalize(),
            createdNames);
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

    private static bool LayerMatches(
        LayerTableRecord layer,
        Transaction transaction,
        ElementLayerStyle style) =>
        layer.Color.ColorIndex == style.ColorIndex &&
        string.Equals(
            GetCanonicalLinetypeName(transaction, layer.LinetypeObjectId),
            CadLinetypeNames.Normalize(style.LinetypeName),
            StringComparison.OrdinalIgnoreCase);

    private static bool LayerPresetMatches(
        CadLayerPreset preset,
        ElementLayerStyle style,
        IReadOnlyDictionary<string, double> persistedScaleByLayer)
    {
        if (preset.AciColorIndex != style.ColorIndex ||
            !string.Equals(
                preset.LinetypeName,
                CadLinetypeNames.Normalize(style.LinetypeName),
                StringComparison.OrdinalIgnoreCase) ||
            preset.HasMixedEntityLinetypeScales)
        {
            return false;
        }

        var candidateScale = preset.UniformEntityLinetypeScale;
        if (candidateScale is null &&
            persistedScaleByLayer.TryGetValue(preset.Name, out var persistedScale))
        {
            candidateScale = persistedScale;
        }

        return candidateScale is not null &&
            Math.Abs(candidateScale.Value - style.LinetypeScale) <=
            CadLayerScaleHydrationRules.ComparisonTolerance;
    }

    private static ElementLayerStyle CloneStyle(
        ElementLayerStyle style,
        string layerName) =>
        new(
            style.ElementType,
            layerName,
            style.ColorIndex,
            style.LinetypeName,
            style.LinetypeScale);

    private sealed record LayerEnsureResult(
        ObjectId LayerId,
        string AppliedLinetypeName,
        bool PreservedConflict,
        bool Changed);

    internal sealed record NewOnlyLayerResolutionResult(
        ElementLayerProfile Profile,
        IReadOnlyList<string> CreatedLayerNames);
}
