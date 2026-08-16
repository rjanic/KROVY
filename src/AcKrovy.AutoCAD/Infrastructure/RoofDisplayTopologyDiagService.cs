#if DEBUG
using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only roof display / GROUP topology probe. Manual on-demand command only
/// (AK_DEV_ROOF_GROUP_TOPOLOGY_DIAG). No automatic live-path instrumentation.
/// </summary>
internal static class RoofDisplayTopologyDiagService
{
    private const string Banner = "AK_DEV_ROOF_GROUP_TOPOLOGY_DIAG";

    public static void RunGroupTopologyDiag()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        Write(editor, $"{Banner}: DEVELOPMENT-ONLY topology probe (read-only)");
        var selection = editor.GetEntity(
            "\nSelect roof source Polyline or any permanent display Line: ");
        if (selection.Status != PromptStatus.OK)
        {
            Write(editor, $"{Banner}: cancelled");
            return;
        }

        using var transaction = document.Database.TransactionManager.StartTransaction();
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                selection.ObjectId,
                OpenMode.ForRead,
                out var entity,
                document.Database) ||
            entity is null)
        {
            Write(editor, $"{Banner}: entity unavailable");
            return;
        }

        ObjectId ownerId;
        if (entity is Polyline polyline &&
            RoofDefinitionStore.Read(polyline).Data is not null)
        {
            ownerId = polyline.ObjectId;
        }
        else
        {
            var resolution = RoofOwnerSelectionResolver.Resolve(
                document.Database,
                transaction,
                selection.ObjectId);
            if (!resolution.IsResolved)
            {
                Write(editor, $"{Banner}: could not resolve roof owner");
                return;
            }

            ownerId = resolution.OwnerId;
        }

        WriteTopology(editor, document.Database, transaction, ownerId, Banner);
        transaction.Commit();
    }

    private static void WriteTopology(
        Editor editor,
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string banner)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            Write(editor, $"{banner}: owner polyline unavailable");
            return;
        }

        var ownerReference = owner.Handle.ToString();
        Write(editor, $"{banner}: source ObjectId={ownerId} Handle={ownerReference}");
        Write(editor, $"{banner}: source vertices=[{FormatPolylineVertices(owner)}]");

        var classification = ClassifyOwner(owner);
        Write(
            editor,
            $"{banner}: sourceClassify={classification.Kind} " +
            $"sourceMatchesStoredDefinition=" +
            $"{classification.Kind == RoofSourceChangeKind.RigidEquivalent}");

        if (!TryGetExpectedDisplay(owner, out var edges, out var signature))
        {
            Write(editor, $"{banner}: expected display unavailable");
            return;
        }

        var inspection = RoofDisplayService.Inspect(
            database,
            transaction,
            ownerId,
            ownerReference,
            edges,
            signature);
        Write(
            editor,
            $"{banner}: validatorState={inspection.Validation.State} " +
            $"issues={inspection.Validation.Issues} lifecycle={inspection.Lifecycle} " +
            $"childCount={inspection.ChildIds.Count} groupCurrent={inspection.Group.IsCurrent} " +
            $"groupName={inspection.Group.GroupName ?? "<none>"}");

        var displayRecords = ScanDisplayChildren(database, transaction);
        var ownerMatched = displayRecords
            .Where(record => string.Equals(
                record.EffectiveOwner,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var inspectedSet = new HashSet<ObjectId>(inspection.ChildIds);
        var validCount = 0;
        var staleCount = 0;
        foreach (var record in displayRecords)
        {
            var associated =
                inspectedSet.Contains(record.Id) ||
                string.Equals(
                    record.EffectiveOwner,
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase);
            if (!associated)
            {
                continue;
            }

            var isValidChild =
                inspectedSet.Contains(record.Id) &&
                inspection.Validation.IsCurrent;
            if (isValidChild)
            {
                validCount++;
            }
            else
            {
                staleCount++;
            }

            Write(
                editor,
                $"{banner}: display id={record.Id} handle={record.Handle} " +
                $"role={record.Role} effectiveOwner={record.EffectiveOwner} " +
                $"raw1005={record.Raw1005 ?? "<none>"} signature={record.Signature ?? "<none>"} " +
                $"erased={record.Erased} validatorScoped={inspectedSet.Contains(record.Id)}");
        }

        Write(
            editor,
            $"{banner}: counts validDisplay={validCount} staleDisplay={staleCount} " +
            $"ownerMatchedDisplay={ownerMatched.Count} " +
            $"totalRoofDisplayLinesScanned={displayRecords.Count}");

        var groups = EnumerateRoofRelatedGroups(database, transaction, ownerId, ownerMatched);
        var sourceGroupCount = 0;
        foreach (var group in groups)
        {
            if (group.ContainsSource)
            {
                sourceGroupCount++;
            }

            Write(
                editor,
                $"{banner}: group name={group.Name} id={group.GroupId} " +
                $"memberCount={group.MemberCount} containsSource={group.ContainsSource} " +
                $"members=[{string.Join("; ", group.MemberSummaries)}]");
        }

        Write(
            editor,
            $"{banner}: sourceBelongsToMultipleGroups={sourceGroupCount > 1} " +
            $"akRoofOrRelatedGroupCount={groups.Count}");
    }

    private static RoofSourceChangeClassification ClassifyOwner(Polyline polyline)
    {
        var stored = RoofDefinitionStore.Read(polyline);
        if (stored.Data is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.None,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        var input = RoofPolylineExtractor.Extract(polyline);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.Unsupported,
                null,
                RoofDefinitionRestoreError.StaleFootprint);
        }

        return RoofDefinitionPersistence.Classify(input, validation.Footprint, stored.Data);
    }

    private static bool TryGetExpectedDisplay(
        Polyline owner,
        out IReadOnlyList<RoofDisplayEdge> edges,
        out string signature)
    {
        edges = Array.Empty<RoofDisplayEdge>();
        signature = string.Empty;
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        var stored = RoofDefinitionStore.Read(owner);
        if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
        {
            return false;
        }

        var restored = RoofDefinitionPersistence.Restore(
            input,
            validation.Footprint,
            stored.Data);
        if (!restored.IsValid || restored.Geometry is null)
        {
            return false;
        }

        edges = SimpleGableRoofWireframe.Create(
            restored.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        return true;
    }

    private static List<DisplayRecord> ScanDisplayChildren(
        Database database,
        Transaction transaction)
    {
        var records = new List<DisplayRecord>();
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
                entity is null)
            {
                continue;
            }

            var stored = RoofDisplayStore.Read(entity);
            if (!stored.Exists)
            {
                continue;
            }

            records.Add(new DisplayRecord(
                id,
                entity.Handle.ToString(),
                stored.Data?.Role.ToString() ?? "<none>",
                stored.OwnerReference ?? "<none>",
                TryReadRaw1005(entity),
                stored.Data?.GenerationSignature,
                entity.IsErased));
        }

        return records;
    }

    private static string? TryReadRaw1005(Entity entity)
    {
        try
        {
            using var xdata = entity.GetXDataForApplication(RoofDisplayStore.RegAppName);
            if (xdata is null)
            {
                return null;
            }

            foreach (var typed in xdata.AsArray())
            {
                if (typed.TypeCode == (int)DxfCode.ExtendedDataHandle)
                {
                    return Convert.ToString(typed.Value, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }

        return null;
    }

    private static List<GroupRecord> EnumerateRoofRelatedGroups(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<DisplayRecord> ownerMatchedDisplay)
    {
        var results = new List<GroupRecord>();
        if (!TryOpenGroupDictionary(database, transaction, out var dictionary) ||
            dictionary is null)
        {
            return results;
        }

        var relatedIds = new HashSet<ObjectId>(ownerMatchedDisplay.Select(record => record.Id))
        {
            ownerId
        };

        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead) is not Group group)
            {
                continue;
            }

            var members = group.GetAllEntityIds();
            var containsSource = members.Contains(ownerId);
            var overlapsDisplay = members.Any(relatedIds.Contains);
            var isAkRoof = entry.Key.StartsWith(
                RoofDisplayGroupService.GroupNamePrefix,
                StringComparison.OrdinalIgnoreCase);
            if (!containsSource && !overlapsDisplay && !isAkRoof)
            {
                continue;
            }

            if (!containsSource && !overlapsDisplay && isAkRoof)
            {
                // List only AK_ROOF groups that involve this owner/display set.
                continue;
            }

            var summaries = new List<string>(members.Length);
            foreach (var memberId in members)
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        memberId,
                        OpenMode.ForRead,
                        out var member,
                        database) ||
                    member is null)
                {
                    summaries.Add($"{memberId}:<unavailable>");
                    continue;
                }

                var kind = member switch
                {
                    Polyline => "Polyline",
                    Line => "Line",
                    _ => member.GetType().Name,
                };
                summaries.Add($"{kind}:{member.Handle}");
            }

            results.Add(new GroupRecord(
                entry.Key,
                entry.Value,
                members.Length,
                containsSource,
                summaries));
        }

        return results;
    }

    private static bool TryOpenGroupDictionary(
        Database database,
        Transaction transaction,
        out DBDictionary? dictionary)
    {
        dictionary = null;
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains("ACAD_GROUP"))
        {
            return false;
        }

        dictionary = (DBDictionary)transaction.GetObject(
            namedObjects.GetAt("ACAD_GROUP"),
            OpenMode.ForRead);
        return true;
    }

    private static string FormatPolylineVertices(Polyline polyline)
    {
        var parts = new List<string>(polyline.NumberOfVertices);
        for (var i = 0; i < polyline.NumberOfVertices; i++)
        {
            var p = polyline.GetPoint3dAt(i);
            parts.Add(string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.###},{1:0.###},{2:0.###})",
                p.X,
                p.Y,
                p.Z));
        }

        return string.Join(" ", parts);
    }

    private static void Write(Editor? editor, string message)
    {
        editor?.WriteMessage("\n" + message);
    }

    private readonly record struct DisplayRecord(
        ObjectId Id,
        string Handle,
        string Role,
        string EffectiveOwner,
        string? Raw1005,
        string? Signature,
        bool Erased);

    private sealed record GroupRecord(
        string Name,
        ObjectId GroupId,
        int MemberCount,
        bool ContainsSource,
        IReadOnlyList<string> MemberSummaries);
}
#endif
