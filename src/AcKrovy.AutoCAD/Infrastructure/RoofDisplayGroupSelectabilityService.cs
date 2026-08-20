using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class RoofDisplayGroupSelectabilityService
{
    public static bool ApplyForOwner(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (ownerId.IsNull ||
            !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            return false;
        }

        var editState = RoofDefinitionStore.Read(owner).Data?.EditState ?? RoofEditState.Locked;
        return TryApplyForOwner(database, transaction, ownerId, editState);
    }

    public static bool ReconcileAllRoofOwners(Database database, Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        var changed = false;
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var polyline,
                    database) ||
                polyline is null ||
                RoofDefinitionStore.Read(polyline).Data is null)
            {
                continue;
            }

            if (TryApplyForOwner(database, transaction, id, RoofDefinitionStore.Read(polyline).Data!.EditState))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool TryApplyForOwner(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        RoofEditState editState)
    {
        var desired = RoofDisplayGroupSelectabilityRules.ShouldEnableGroupSelection(editState);
        var groupName = "-";
        var groupObjectId = "-";
        bool? readBefore = null;
        bool? readAfter = null;
        var result = "ok";
        if (!RoofDisplayGroupService.TryOpenCanonicalGroup(
                database,
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var group) ||
            group is null)
        {
#if DEBUG
            WriteSelectabilityDiag(database, transaction, ownerId, groupName, editState, readBefore, desired, readAfter, groupObjectId, "canonical-group-missing");
#endif
            return false;
        }

        groupName = RoofDisplayGroupService.BuildCanonicalGroupName(transaction, ownerId);
        groupObjectId = group.ObjectId.Handle.ToString();
        readBefore = group.Selectable;
        if (group.Selectable != desired)
        {
            group.Selectable = desired;
        }
        else
        {
            result = "already-current";
        }

        readAfter = group.Selectable;
        var pruned = RoofDisplayGroupService.PruneStaleRoofGroupsContainingCanonicalMembers(
            database,
            transaction,
            ownerId);
        if (pruned > 0)
        {
            result = $"ok;pruned={pruned.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
#if DEBUG
        WriteSelectabilityDiag(database, transaction, ownerId, groupName, editState, readBefore, desired, readAfter, groupObjectId, result);
        WriteGroupMembershipDiagnostics(database, transaction, ownerId);
#endif
        return readBefore != readAfter || pruned > 0;
    }

#if DEBUG
    internal static void WriteGroupMembershipDiagnostics(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!TryResolveOwnerHandle(database, transaction, ownerId, out var ownerHandle))
        {
            return;
        }

        var observations = RoofDisplayGroupService.CollectGroupsContainingCanonicalMembers(
            database,
            transaction,
            ownerId);
        foreach (var observation in observations)
        {
            RoofDisplayGroupSelectabilityDiag.WriteGroup(
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor,
                ownerHandle,
                observation.GroupName,
                observation.GroupObjectId,
                observation.Selectable,
                observation.MemberCount,
                observation.IsCanonical,
                observation.IsStaleKrovyDuplicate);
        }
    }

    private static void WriteSelectabilityDiag(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        string groupName,
        RoofEditState editState,
        bool? selectableReadBefore,
        bool selectableWritten,
        bool? selectableReadAfter,
        string groupObjectId,
        string result)
    {
        if (!TryResolveOwnerHandle(database, transaction, ownerId, out var ownerHandle))
        {
            ownerHandle = ownerId.Handle.ToString();
        }

        RoofDisplayGroupSelectabilityDiag.Write(
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor,
            ownerHandle,
            groupName,
            editState,
            selectableReadBefore,
            selectableWritten,
            selectableReadAfter,
            groupObjectId,
            result,
            TryReadPickstyle());
    }

    private static bool TryResolveOwnerHandle(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        out string ownerHandle)
    {
        ownerHandle = string.Empty;
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var ownerEntity,
                database) ||
            ownerEntity is null)
        {
            return false;
        }

        ownerHandle = ownerEntity.Handle.ToString();
        return true;
    }

    private static string TryReadPickstyle()
    {
        try
        {
            return Autodesk.AutoCAD.ApplicationServices.Application
                .GetSystemVariable("PICKSTYLE")
                ?.ToString() ?? "-";
        }
        catch
        {
            return "-";
        }
    }
#endif
}

#if DEBUG
internal static class RoofDisplayGroupSelectabilityDiag
{
    public static void Write(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        string groupName,
        RoofEditState editState,
        bool? selectableReadBefore,
        bool selectableWritten,
        bool? selectableReadAfter,
        string groupObjectId,
        string result,
        string pickstyle)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GROUP_SELECTABILITY" +
            $" owner={owner}" +
            $" groupName={groupName}" +
            $" editState={editState}" +
            $" selectableReadBefore={FormatBool(selectableReadBefore)}" +
            $" selectableWritten={selectableWritten.ToString().ToLowerInvariant()}" +
            $" selectableReadAfter={FormatBool(selectableReadAfter)}" +
            $" groupObjectId={groupObjectId}" +
            $" pickstyle={pickstyle}" +
            $" result={result}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    public static void WriteGroup(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        string groupName,
        string groupObjectId,
        bool selectable,
        int memberCount,
        bool isCanonical,
        bool isStaleKrovyDuplicate)
    {
        if (editor is null)
        {
            return;
        }

        var line =
            "ROOF_GROUP_MEMBER_GROUP" +
            $" owner={owner}" +
            $" groupName={groupName}" +
            $" groupObjectId={groupObjectId}" +
            $" selectable={selectable.ToString().ToLowerInvariant()}" +
            $" memberCount={memberCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $" canonical={isCanonical.ToString().ToLowerInvariant()}" +
            $" staleKrovyDuplicate={isStaleKrovyDuplicate.ToString().ToLowerInvariant()}";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }

    private static string FormatBool(bool? value) =>
        value.HasValue ? value.Value.ToString().ToLowerInvariant() : "-";
}
#endif
