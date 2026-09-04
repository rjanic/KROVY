using System.Collections.ObjectModel;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofExternalImportPreflightService
{
    private static readonly HashSet<string> KnownRegApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ElementDataStore.RegAppName,
        RoofDefinitionStore.RegAppName,
        RoofDisplayStore.RegAppName,
        RoofGeneratedTimberStore.RegAppName,
        RoofGeneratedTimberStore.LinkRegAppName,
        RoofAttachedManualTimberStore.RegAppName,
        ElementLabelStore.RegAppName,
        SlopeArrowStore.RegAppName,
        SlopeAngleTextStore.RegAppName,
        PostFootprintPerpendicularAnnotationStore.RegAppName,
        RoofUnlockIndicatorStore.RegAppName,
    };

    public static RoofExternalImportManifest Create(
        Database sourceDatabase,
        string sourceIdentity,
        string destinationBlockName)
    {
        ArgumentNullException.ThrowIfNull(sourceDatabase);
        using var transaction = sourceDatabase.TransactionManager.StartOpenCloseTransaction();
        var blockTable = (BlockTable)transaction.GetObject(sourceDatabase.BlockTableId, OpenMode.ForRead);
        var sourceIds = new HashSet<ObjectId>();
        var graph = new Dictionary<ObjectId, IReadOnlyList<ObjectId>>();
        var reachable = new HashSet<ObjectId>();
        var timbers = new List<RoofExternalImportObject>();
        var annotations = new List<RoofExternalImportObject>();
        var support = new List<ObjectId>();
        var malformed = false;
        var hasRoofDefinition = false;
        var hasRoofOwnedState = false;
        var conflicts = false;

        var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDatabase);
        CollectReachable(transaction, modelSpaceId, reachable, graph);
        malformed |= ContainsKrovyNamedDictionaryMarker(transaction, sourceDatabase);

        foreach (ObjectId blockId in blockTable)
        {
            sourceIds.Add(blockId);
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            malformed |= HasKrovyMetadataOnNonEntity(block, transaction);
            if (!block.IsLayout && !reachable.Contains(blockId))
            {
                if (IsRelationshipConfirmedSupport(transaction, sourceDatabase, block))
                {
                    support.Add(blockId);
                }
                else
                {
                    malformed = true;
                }
            }

            foreach (ObjectId entityId in block)
            {
                sourceIds.Add(entityId);
                var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                var regApps = ReadRegApps(entity);
                if (regApps.Any(IsUnknownKrovyRegApp))
                {
                    malformed = true;
                }
                malformed |= HasUnknownEntityKrovyDictionary(entity, transaction);

                var roof = RoofDefinitionStore.Read(entity);
                hasRoofDefinition |= roof.Data is not null;
                malformed |= roof.Exists && roof.Data is null;

                var display = RoofDisplayStore.Read(entity);
                hasRoofOwnedState |= display.Exists;
                malformed |= display.Exists && display.Data is null;

                if (regApps.Contains(RoofUnlockIndicatorStore.RegAppName))
                {
                    hasRoofOwnedState = true;
                    malformed |= string.IsNullOrWhiteSpace(
                        RoofUnlockIndicatorStore.TryReadOwnerReference(entity));
                }

                var generated = RoofGeneratedTimberStore.Read(entity);
                var attached = RoofAttachedManualTimberStore.Read(entity);
                var hasLegacyGenericMarker = HasLegacyGenericMarker(
                    entity,
                    transaction,
                    out var legacyAmbiguous);
                var hasGenericMarker = regApps.Contains(ElementDataStore.RegAppName) ||
                    hasLegacyGenericMarker;
                malformed |= legacyAmbiguous;
                var portableValid = !regApps.Contains(ElementDataStore.RegAppName) ||
                    (ElementDataStore.TryReadPortableXData(entity, out var portableData) &&
                     portableData is not null && TimberElementDataVersioning.IsSupported(portableData));
                var legacyValid = !hasLegacyGenericMarker ||
                    (ElementDataStore.TryReadLegacyExtensionDictionary(entity, transaction, out var legacyData) &&
                     legacyData is not null && TimberElementDataVersioning.IsSupported(legacyData));
                var hasGeneric = hasGenericMarker && portableValid && legacyValid;
                malformed |= hasGenericMarker && !hasGeneric;
                malformed |= generated.Exists && generated.Data is null;
                malformed |= regApps.Contains(RoofGeneratedTimberStore.LinkRegAppName) &&
                    generated.Data is null;
                malformed |= attached.Exists && attached.Data is null;
                conflicts |= generated.Data is not null && attached.Data is not null;

                if ((generated.Data is not null || attached.Data is not null || hasGeneric) &&
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
                {
                    malformed = true;
                }

                if (generated.Data is not null)
                {
                    timbers.Add(new(entityId, RoofExternalImportObjectRole.Generated));
                }
                else if (attached.Data is not null)
                {
                    timbers.Add(new(entityId, RoofExternalImportObjectRole.AttachedManual));
                }
                else if (hasGeneric)
                {
                    timbers.Add(new(entityId, RoofExternalImportObjectRole.GenericTimber));
                }

                var annotationCountBefore = annotations.Count;
                malformed |= ClassifyAnnotation(entity, entityId, regApps, annotations);
                malformed |= annotations.Count - annotationCountBefore > 1;
            }
        }

        var timberHandles = timbers
            .Select(item => item.SourceId.Handle.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphanAnnotation = annotations.Any(item =>
            string.IsNullOrWhiteSpace(item.SourceHandle) ||
            !timberHandles.Contains(item.SourceHandle));
        var decision = RoofExternalImportPolicy.Classify(new(
            malformed,
            hasRoofDefinition,
            hasRoofOwnedState && !hasRoofDefinition,
            conflicts,
            orphanAnnotation,
            timbers.Count,
            annotations.Count));

        var userNames = reachable
            .Where(id => id != modelSpaceId)
            .Select(id => ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reachableEntities = reachable.SelectMany(id =>
            ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>());
        var expected = reachable
            .Where(id => id != modelSpaceId)
            .Concat(reachableEntities)
            .Distinct()
            .ToArray();

        return new(
            sourceIdentity,
            destinationBlockName,
            modelSpaceId,
            Array.AsReadOnly(sourceIds.ToArray()),
            new ReadOnlyDictionary<ObjectId, IReadOnlyList<ObjectId>>(graph),
            Array.AsReadOnly(timbers.ToArray()),
            Array.AsReadOnly(annotations.ToArray()),
            Array.AsReadOnly(support.ToArray()),
            Array.AsReadOnly(userNames),
            Array.AsReadOnly(expected),
            decision);
    }

    private static bool ClassifyAnnotation(
        Entity entity,
        ObjectId entityId,
        HashSet<string> regApps,
        List<RoofExternalImportObject> annotations)
    {
        var annotationMalformed = false;
        AddAnnotation(regApps, ElementLabelStore.RegAppName,
            () => ElementLabelStore.TryRead(entity, out var data) && data is not null &&
                  data.SchemaVersion > 0 && data.SchemaVersion <= AutoCadFramedBlockContentProductionPolicy.LabelMetadataSchemaVersion
                ? data.SourceHandle : null,
            RoofExternalImportObjectRole.ElementLabel);
        AddAnnotation(regApps, SlopeArrowStore.RegAppName,
            () => SlopeArrowStore.TryRead(entity, out var data) && data is { SchemaVersion: 1 }
                ? data.SourceHandle : null,
            RoofExternalImportObjectRole.SlopeArrow);
        AddAnnotation(regApps, SlopeAngleTextStore.RegAppName,
            () => SlopeAngleTextStore.TryRead(entity, out var data) && data is { SchemaVersion: 1 }
                ? data.SourceHandle : null,
            RoofExternalImportObjectRole.SlopeAngleText);
        AddAnnotation(regApps, PostFootprintPerpendicularAnnotationStore.RegAppName,
            () => PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var data) && data is { SchemaVersion: 1 }
                ? data.SourceHandle : null,
            RoofExternalImportObjectRole.PostFootprintPerpendicular);
        return annotationMalformed;

        void AddAnnotation(
            HashSet<string> markers,
            string marker,
            Func<string?> read,
            RoofExternalImportObjectRole role)
        {
            if (!markers.Contains(marker))
            {
                return;
            }

            var sourceHandle = read();
            if (string.IsNullOrWhiteSpace(sourceHandle))
            {
                annotationMalformed = true;
                return;
            }

            annotations.Add(new(entityId, role, sourceHandle));
        }
    }

    private static HashSet<string> ReadRegApps(Entity entity)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var xdata = entity.XData;
        if (xdata is null)
        {
            return names;
        }

        foreach (var value in xdata.AsArray())
        {
            if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                value.Value is string name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static bool IsUnknownKrovyRegApp(string name) =>
        (name.StartsWith("DECORAIR_ACADKROVY", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("ACAD_KROVY", StringComparison.OrdinalIgnoreCase)) &&
        !KnownRegApps.Contains(name);

    private static bool ContainsKrovyNamedDictionaryMarker(
        Transaction transaction,
        Database database)
    {
        var root = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        return ContainsKrovyDictionaryMarker(transaction, root, new HashSet<ObjectId>());
    }

    private static bool HasKrovyMetadataOnNonEntity(
        DBObject value,
        Transaction transaction)
    {
        using (var xdata = value.XData)
        {
            if (xdata is not null && xdata.AsArray().Any(item =>
                    item.TypeCode == (int)DxfCode.ExtendedDataRegAppName &&
                    item.Value is string name &&
                    (name.StartsWith("DECORAIR_ACADKROVY", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("ACAD_KROVY", StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return !value.ExtensionDictionary.IsNull &&
               transaction.GetObject(value.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dictionary &&
               ContainsKrovyDictionaryMarker(transaction, dictionary, new HashSet<ObjectId>());
    }

    private static bool HasUnknownEntityKrovyDictionary(
        Entity entity,
        Transaction transaction)
    {
        if (entity.ExtensionDictionary.IsNull ||
            transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary root)
        {
            return false;
        }

        foreach (DBDictionaryEntry entry in root)
        {
            if (entry.Key.StartsWith("DECORAIR_ACADKROVY", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry.Key.StartsWith("ACAD_KROVY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.Key, ElementDataStore.LegacyApplicationDictionaryName, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(entry.Key, ElementDataStore.LegacyApplicationDictionaryName, StringComparison.Ordinal) &&
                transaction.GetObject(entry.Value, OpenMode.ForRead) is DBDictionary child &&
                ContainsKrovyDictionaryMarker(transaction, child, new HashSet<ObjectId>()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsKrovyDictionaryMarker(
        Transaction transaction,
        DBDictionary dictionary,
        HashSet<ObjectId> visited)
    {
        if (!visited.Add(dictionary.ObjectId))
        {
            return false;
        }

        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (entry.Key.StartsWith("DECORAIR_ACADKROVY", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.StartsWith("ACAD_KROVY", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is DBDictionary child &&
                ContainsKrovyDictionaryMarker(transaction, child, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLegacyGenericMarker(
        Entity entity,
        Transaction transaction,
        out bool ambiguous)
    {
        ambiguous = false;
        if (entity.ExtensionDictionary.IsNull ||
            transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary root ||
            !root.Contains(ElementDataStore.LegacyApplicationDictionaryName))
        {
            return false;
        }

        if (transaction.GetObject(
                root.GetAt(ElementDataStore.LegacyApplicationDictionaryName),
                OpenMode.ForRead) is not DBDictionary app)
        {
            ambiguous = true;
            return false;
        }

        ambiguous = app.Cast<DBDictionaryEntry>().Any(entry =>
            !string.Equals(entry.Key, ElementDataStore.LegacyElementDataRecordName, StringComparison.Ordinal));
        return app.Contains(ElementDataStore.LegacyElementDataRecordName);
    }

    private static void CollectReachable(
        Transaction transaction,
        ObjectId blockId,
        HashSet<ObjectId> visited,
        Dictionary<ObjectId, IReadOnlyList<ObjectId>> graph)
    {
        if (!visited.Add(blockId))
        {
            return;
        }

        var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
        var dependencies = block.Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead) as BlockReference)
            .Where(reference => reference is not null)
            .Select(reference => reference!.BlockTableRecord)
            .Distinct()
            .ToArray();
        graph[blockId] = Array.AsReadOnly(dependencies);
        foreach (var dependency in dependencies)
        {
            CollectReachable(transaction, dependency, visited, graph);
        }
    }

    private static bool IsRelationshipConfirmedSupport(
        Transaction transaction,
        Database database,
        BlockTableRecord block)
    {
        var containedBlockReferenceCount = block.Cast<ObjectId>()
            .Count(id => transaction.GetObject(id, OpenMode.ForRead) is BlockReference);
        var directBlockReferenceCount = block.GetBlockReferenceIds(true, false).Count;
        var styleReference = HasDimStyleReference(transaction, database, block.ObjectId) ||
            HasMLeaderStyleReference(transaction, database, block.ObjectId) ||
            database.Dimblk == block.ObjectId || database.Dimblk1 == block.ObjectId ||
            database.Dimblk2 == block.ObjectId || database.Dimldrblk == block.ObjectId;
        var reachability = FindSpaceReachability(transaction, database, block.ObjectId);
        return styleReference &&
               containedBlockReferenceCount == 0 &&
               directBlockReferenceCount == 0 &&
               !reachability.Model &&
               !reachability.Paper;
    }

    private static bool HasDimStyleReference(Transaction transaction, Database database, ObjectId blockId)
    {
        var table = (DimStyleTable)transaction.GetObject(database.DimStyleTableId, OpenMode.ForRead);
        return table.Cast<ObjectId>().Any(id =>
        {
            var style = (DimStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            return style.Dimblk == blockId || style.Dimblk1 == blockId ||
                   style.Dimblk2 == blockId || style.Dimldrblk == blockId;
        });
    }

    private static bool HasMLeaderStyleReference(Transaction transaction, Database database, ObjectId blockId)
    {
        var dictionary = (DBDictionary)transaction.GetObject(database.MLeaderStyleDictionaryId, OpenMode.ForRead);
        return dictionary.Cast<DBDictionaryEntry>().Any(entry =>
            transaction.GetObject(entry.Value, OpenMode.ForRead) is MLeaderStyle style &&
            style.ArrowSymbolId == blockId);
    }

    private static (bool Model, bool Paper) FindSpaceReachability(
        Transaction transaction,
        Database database,
        ObjectId target)
    {
        var modelId = SymbolUtilityServices.GetBlockModelSpaceId(database);
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = false;
        var paper = false;
        foreach (ObjectId id in table)
        {
            var space = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (!space.IsLayout)
            {
                continue;
            }

            var found = IsReachable(transaction, space, target, new HashSet<ObjectId> { id });
            model |= id == modelId && found;
            paper |= id != modelId && found;
        }

        return (model, paper);
    }

    private static bool IsReachable(
        Transaction transaction,
        BlockTableRecord block,
        ObjectId target,
        HashSet<ObjectId> visited)
    {
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not BlockReference reference)
            {
                continue;
            }

            if (reference.BlockTableRecord == target)
            {
                return true;
            }

            if (visited.Add(reference.BlockTableRecord) &&
                IsReachable(
                    transaction,
                    (BlockTableRecord)transaction.GetObject(reference.BlockTableRecord, OpenMode.ForRead),
                    target,
                    visited))
            {
                return true;
            }
        }

        return false;
    }
}
