using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofAssemblyGroupSyncService
{
    public static bool TryFinalizeRestoredSources(Document document,
        IReadOnlyCollection<ObjectId> ownerIds, string? globalCommandName)
    {
        // Called with the existing document lock after the source-repair transaction
        // is disposed. Do not defer this work beyond the native command undo scope.
        if (AcKrovy.Core.Services.LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !AcKrovy.Core.Services.Roofs.RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
            return false;
        var groupIds = new Dictionary<ObjectId, ObjectId>();
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var changed = false;
            foreach (var ownerId in ownerIds.Distinct())
            {
                if (!RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database, transaction,
                        ownerId, OpenMode.ForRead, out var group) || group is null) return false;
                groupIds.Add(ownerId, group.ObjectId);
#if DEBUG
                RoofPhysical3DHostDiagnostics.OwnerCounts(document.Database, transaction,
                    ownerId.Handle.ToString(), "locked-source-group-finalize-before");
#endif
                if (!IsCurrent(document.Database, transaction, ownerId))
                {
                    if (!TryRemoveSurplusSourceSlots(document.Database, transaction, ownerId, group)) return false;
                    changed = true;
                }
                if (!IsCurrent(document.Database, transaction, ownerId)) return false;
#if DEBUG
                RoofPhysical3DHostDiagnostics.OwnerCounts(document.Database, transaction,
                    ownerId.Handle.ToString(), "locked-source-group-finalize-after:provisional");
#endif
            }
            if (changed) transaction.Commit();
        }
        // Verify the committed, closed transaction, not only its provisional view.
        using var verify = document.Database.TransactionManager.StartTransaction();
        foreach (var ownerId in ownerIds.Distinct())
        {
#if DEBUG
            RoofPhysical3DHostDiagnostics.OwnerCounts(document.Database, verify,
                ownerId.Handle.ToString(), "locked-source-group-finalize-committed");
#endif
            if (!IsCurrent(document.Database, verify, ownerId) ||
                !RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database, verify,
                    ownerId, OpenMode.ForRead, out var group) || group is null ||
                group.ObjectId != groupIds[ownerId]) return false;
#if DEBUG
            if (RoofDisplayErasePreCommandMapService.TryGetSourceState(ownerId, out var state))
                RoofGeneratedMemberManualEditDiag.WriteSourceEraseRepair(document.Editor, state.OwnerHandle,
                    sourceRestored: !ownerId.IsErased, sameObjectId: ownerId == state.OwnerId,
                    sameHandle: string.Equals(ownerId.Handle.ToString(), state.OwnerHandle, StringComparison.OrdinalIgnoreCase),
                    displayRebuilt: true, groupMembers: group.GetAllEntityIds().Length, canonical: true,
                    result: "Recovered|ok");
#endif
        }
        return true;
    }

    private static bool IsCurrent(Database database, Transaction transaction, ObjectId ownerId) =>
        AutoCadObjectIdAccess.TryGetObject<Polyline>(transaction, ownerId, OpenMode.ForRead,
            out var owner, database) && owner is not null &&
        RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(database, transaction, owner,
            out var displayIds) && RoofDisplayGroupService.Inspect(database, transaction, ownerId, displayIds).IsCurrent;

    private static bool TryRemoveSurplusSourceSlots(Database database, Transaction transaction,
        ObjectId ownerId, Group group)
    {
        var before = group.GetAllEntityIds();
        var surplus = AcKrovy.Core.Services.Roofs.RoofAssemblyGroupMembershipRules.SurplusMemberIndices(before, ownerId);
        if (surplus.Count == 0 ||
            !AutoCadObjectIdAccess.TryGetObject<Polyline>(transaction, ownerId, OpenMode.ForRead,
                out var owner, database) || owner is null ||
            !RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(database, transaction, owner,
                out var displayIds) ||
            !RoofAssemblyGroupMemberCollector.TryCollect(database, transaction, ownerId, displayIds,
                out var collected) || collected is null) return false;
        var uniqueBefore = new HashSet<ObjectId>(before);
        // Fail closed for missing/foreign unique members or another kind of duplicate.
        // This correction removes only surplus slots for the proven restored source.
        if (!uniqueBefore.SetEquals(collected.MemberIds) ||
            before.Length - uniqueBefore.Count != surplus.Count) return false;
        group.UpgradeOpen();
        foreach (var index in surplus.Reverse())
        {
            var current = group.GetAllEntityIds();
            if (index >= current.Length || current[index] != ownerId ||
                current.Count(id => id == ownerId) <= 1) return false;
            group.RemoveAt(index);
#if DEBUG
            RoofPhysical3DHostDiagnostics.GroupMutation(database, ownerId.Handle.ToString(),
                "remove-at-source-slot", group.Handle.ToString(), ownerId.Handle.ToString(),
                "locked-source-group-finalize:index=" + index);
#endif
            var after = group.GetAllEntityIds();
            if (after.Length != current.Length - 1 || !uniqueBefore.SetEquals(after)) return false;
        }
        return true;
    }

    public static bool TrySyncForOwner(
        Document document,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (ownerId.IsNull)
        {
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                document.Database) ||
            owner is null ||
            RoofDefinitionStore.Read(owner).Data is null)
        {
            return false;
        }

        if (!RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(
            document.Database,
            transaction,
            owner,
            out var displayChildIds))
        {
            return false;
        }

        RoofDisplayGroupService.EnsureGroup(
            document.Database,
            transaction,
            ownerId,
            displayChildIds);
        return true;
    }

    /// <summary>
    /// Facade for detaching timber members (and their annotations) from the canonical
    /// GROUP before they are erased. Keeps the undo stack valid for native U/UNDO.
    /// </summary>
    public static int DetachMembersBeforeErase(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyCollection<ObjectId> timberIds) =>
        RoofDisplayGroupService.DetachMembersBeforeErase(
            database,
            transaction,
            ownerId,
            timberIds);

    public static bool TrySyncForOwnerReference(
        Document document,
        Transaction transaction,
        string ownerReference)
    {
        if (string.IsNullOrWhiteSpace(ownerReference) ||
            !long.TryParse(
                ownerReference,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        var ownerId = document.Database.GetObjectId(false, new Handle(handleValue), 0);
        return !ownerId.IsNull && TrySyncForOwner(document, transaction, ownerId);
    }
}

#if DEBUG
internal static class RoofAssemblyGroupDiag
{
    public static void WriteMembershipSnapshot(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string checkpoint)
    {
        if (editor is null ||
            !AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            return;
        }

        var ownerReference = owner.Handle.ToString();
        if (!RoofDisplayGroupService.TryOpenCanonicalGroup(
                database,
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var group) ||
            group is null)
        {
            WriteSnapshotLine(editor, ownerReference, checkpoint, Array.Empty<string>());
            return;
        }

        var members = group.GetAllEntityIds()
            .Where(id => !id.IsNull && !id.IsErased)
            .OrderBy(id => id.Handle.Value)
            .ToArray();
        var timberHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in members)
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

            var generated = RoofGeneratedTimberStore.Read(entity).Data;
            var structural = RoofStructuralGeneratedStore.Read(entity).Data;
            var automaticPurlin = RoofAutomaticPurlinGeneratedStore.Read(entity).Data;
            var attached = RoofAttachedManualTimberStore.Read(entity).Data;
            if (string.Equals(generated?.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(structural?.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(automaticPurlin?.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attached?.RoofOwnerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
            {
                timberHandles.Add(entity.Handle.ToString());
            }
        }

        var observations = new List<string>(members.Length);
        foreach (var id in members)
        {
            var role = "Unreadable";
            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) &&
                entity is not null)
            {
                role = ResolveMemberRole(entity, ownerId, ownerReference, timberHandles);
            }

            observations.Add(id.Handle.ToString() + ":" + role);
        }

        WriteSnapshotLine(editor, ownerReference, checkpoint, observations);
    }

    public static void WriteSync(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        int generated,
        int structuralGenerated,
        int automaticPurlin,
        int attachedManual,
        int annotations,
        int total,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GROUP_SYNC" +
            $" owner={owner}" +
            $" generated={generated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" structuralGenerated={structuralGenerated.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" automaticPurlin={automaticPurlin.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" attachedManual={attachedManual.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" annotations={annotations.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" total={total.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteSyncPost(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        int expected,
        int actual,
        int duplicates,
        int missing,
        int foreign,
        bool canonical)
    {
        if (editor is null)
        {
            return;
        }

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var line =
            "ROOF_GROUP_SYNC_POST" +
            $" owner={owner}" +
            $" expected={expected.ToString(invariant)}" +
            $" actual={actual.ToString(invariant)}" +
            $" duplicates={duplicates.ToString(invariant)}" +
            $" missing={missing.ToString(invariant)}" +
            $" foreign={foreign.ToString(invariant)}" +
            $" canonical={(canonical ? "1" : "0")}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteMember(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        string handle,
        string role,
        string result)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GROUP_MEMBER" +
            $" owner={owner}" +
            $" handle={handle}" +
            $" role={role}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string ResolveMemberRole(
        Entity entity,
        ObjectId ownerId,
        string ownerReference,
        IReadOnlySet<string> timberHandles)
    {
        if (entity.ObjectId == ownerId)
        {
            return "Owner";
        }
        if (string.Equals(
                RoofDisplayStore.Read(entity).Data?.OwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Display";
        }
        if (string.Equals(
                RoofGeneratedTimberStore.Read(entity).Data?.RoofOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Generated";
        }
        if (string.Equals(
                RoofStructuralGeneratedStore.Read(entity).Data?.RoofOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return "StructuralGenerated";
        }
        if (string.Equals(
                RoofAutomaticPurlinGeneratedStore.Read(entity).Data?.RoofOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return "AutomaticPurlin";
        }
        if (string.Equals(
                RoofAttachedManualTimberStore.Read(entity).Data?.RoofOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return "AttachedManual";
        }
        if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var sourceHandle) &&
            timberHandles.Contains(sourceHandle))
        {
            return "Annotation";
        }

        return "Foreign";
    }

    private static void WriteSnapshotLine(
        Autodesk.AutoCAD.EditorInput.Editor editor,
        string owner,
        string checkpoint,
        IReadOnlyList<string> observations)
    {
        var line =
            "ROOF_GROUP_MEMBERS" +
            $" owner={owner}" +
            $" checkpoint={checkpoint}" +
            $" memberCount={observations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" members={string.Join(",", observations)}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }
}
#endif
