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

        var ownerReference = owner.Handle.ToString();
        var displayChildIds = RoofDisplayService.CollectStructuralDisplayChildIds(
            document.Database,
            transaction,
            ownerId,
            ownerReference);
        if (displayChildIds.Count != RoofDisplayGroupService.ExpectedStructuralDisplayChildCount)
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
    public static void WriteSync(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string owner,
        int generated,
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
}
#endif
