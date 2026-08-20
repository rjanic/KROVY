using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Command-scoped rollback for rejected or failed roof COPY children (locked reject, AttachedManual failure).
/// Removes only objects tied to the clone timber source handle.
/// </summary>
internal static class RoofCopiedChildRollbackService
{
    public static bool TryRollbackCopiedRoofChild(
        Document document,
        Transaction transaction,
        string cloneSourceHandle,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(cloneSourceHandle))
        {
            failureReason = "clone-handle-missing";
            return false;
        }

        var handle = cloneSourceHandle.Trim();
        try
        {
            TimberAnnotationService.DeleteForSourceHandle(
                document.Database,
                transaction,
                handle);

            if (!TryOpenCloneLine(document.Database, transaction, handle, out var entity) ||
                entity is null)
            {
                // Line may already be erased; annotations are the critical residue.
                return true;
            }

            _ = RoofGeneratedTimberStore.TryClear(entity, transaction, out _);
            _ = RoofAttachedManualTimberStore.TryClear(entity, transaction, out _);

            if (!entity.IsErased)
            {
                entity.UpgradeOpen();
                entity.Erase();
            }

#if DEBUG
            RoofGeneratedCopyLifecycleDiag.WriteRollback(
                document.Editor,
                handle,
                "ok");
#endif
            return true;
        }
        catch (System.Exception ex)
        {
            failureReason = ex.GetType().Name;
#if DEBUG
            RoofGeneratedCopyLifecycleDiag.WriteRollback(
                document.Editor,
                handle,
                failureReason);
#endif
            return false;
        }
    }

    private static bool TryOpenCloneLine(
        Database database,
        Transaction transaction,
        string memberKey,
        out Entity? entity)
    {
        entity = null;
        if (!TryParseEntityHandle(database, memberKey, out var objectId))
        {
            return false;
        }

        return AutoCadObjectIdAccess.TryGetObject<Entity>(
            transaction,
            objectId,
            OpenMode.ForWrite,
            out entity,
            database) && entity is not null && !entity.IsErased;
    }

    private static bool TryParseEntityHandle(Database database, string handleText, out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        if (string.IsNullOrWhiteSpace(handleText))
        {
            return false;
        }

        try
        {
            var hex = handleText.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex[2..];
            }

            if (!long.TryParse(
                    hex,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var handleValue))
            {
                return false;
            }

            var handle = new Handle(handleValue);
            objectId = database.GetObjectId(false, handle, 0);
            return !objectId.IsNull;
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
