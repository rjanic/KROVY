using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofAssemblyGroupSyncService
{
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
