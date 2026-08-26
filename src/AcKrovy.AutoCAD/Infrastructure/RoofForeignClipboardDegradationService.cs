using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Atomically converts exact appended foreign/unknown intelligent roof timbers to
/// plain visible geometry. No owner handle is resolved and no drawing-wide search,
/// association, identity initialization, annotation recreation, or group sync runs.
/// </summary>
internal static class RoofForeignClipboardDegradationService
{
    public static RoofForeignClipboardDegradationResult Process(
        Document document,
        string? globalCommandName,
        RoofClipboardPastePayloadSnapshot payload)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(payload);
        var currentPasteAnnotationIds =
            RoofClipboardPasteOwnershipRules.SelectCurrentPasteAnnotations(
                payload.AppendedEntityIds,
                payload.AnnotationIds);
        var cleanupPayload = payload with
        {
            AnnotationIds = currentPasteAnnotationIds,
        };

        try
        {
            var detached = ExecuteCleanup(document, cleanupPayload, eraseTimbers: false);
            var result = new RoofForeignClipboardDegradationResult(
                true,
                false,
                cleanupPayload.IntelligentTimberIds.Count,
                cleanupPayload.AnnotationIds.Count,
                detached,
                "ok");
            Trace(document, globalCommandName, result);
            return result;
        }
        catch (System.Exception cleanupError)
        {
            try
            {
                var detached = ExecuteCleanup(document, cleanupPayload, eraseTimbers: true);
                var result = new RoofForeignClipboardDegradationResult(
                    true,
                    true,
                    cleanupPayload.IntelligentTimberIds.Count,
                    cleanupPayload.AnnotationIds.Count,
                    detached,
                    "fail-closed:" + cleanupError.GetType().Name);
                Trace(document, globalCommandName, result);
                return result;
            }
            catch (System.Exception fallbackError)
            {
                var result = new RoofForeignClipboardDegradationResult(
                    false,
                    true,
                    cleanupPayload.IntelligentTimberIds.Count,
                    cleanupPayload.AnnotationIds.Count,
                    0,
                    "cleanup=" + cleanupError.GetType().Name +
                    ";fallback=" + fallbackError.GetType().Name);
                Trace(document, globalCommandName, result);
                return result;
            }
        }
    }

    private static int ExecuteCleanup(
        Document document,
        RoofClipboardPastePayloadSnapshot payload,
        bool eraseTimbers)
    {
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var targetIds = payload.IntelligentTimberIds
                .Concat(payload.AnnotationIds)
                .Distinct()
                .ToArray();
            var detached = DetachFromProvenCanonicalRoofGroups(
                document.Database,
                transaction,
                targetIds);

            foreach (var timberId in payload.IntelligentTimberIds.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        timberId,
                        OpenMode.ForWrite,
                        out var timber,
                        document.Database) ||
                    timber is null ||
                    timber.IsErased)
                {
                    throw new InvalidOperationException("appended-intelligent-timber-unavailable");
                }

                if (eraseTimbers)
                {
                    timber.Erase();
                    continue;
                }

                timber.Visible = true;
                if (RoofGeneratedTimberStore.Read(timber).Exists &&
                    !RoofGeneratedTimberStore.TryClear(
                        timber,
                        transaction,
                        out var generatedFailure))
                {
                    throw new InvalidOperationException(generatedFailure);
                }

                if (RoofAttachedManualTimberStore.Read(timber).Exists &&
                    !RoofAttachedManualTimberStore.TryClear(
                        timber,
                        transaction,
                        out var attachedFailure))
                {
                    throw new InvalidOperationException(attachedFailure);
                }

                if (!ElementDataStore.TryClear(timber, transaction, out var genericFailure))
                {
                    throw new InvalidOperationException(genericFailure);
                }

                if (RoofGeneratedTimberStore.Read(timber).Exists ||
                    RoofAttachedManualTimberStore.Read(timber).Exists ||
                    ElementDataStore.TryRead(timber, transaction, out _))
                {
                    throw new InvalidOperationException("krovy-metadata-remains");
                }
            }

            foreach (var annotationId in payload.AnnotationIds.Distinct())
            {
                if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        annotationId,
                        OpenMode.ForWrite,
                        out var annotation,
                        document.Database) &&
                    annotation is not null &&
                    !annotation.IsErased)
                {
                    annotation.Erase();
                }
            }

            transaction.Commit();
            return detached;
        }
    }

    private static int DetachFromProvenCanonicalRoofGroups(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> targetIds)
    {
        if (targetIds.Count == 0)
        {
            return 0;
        }

        var targets = targetIds.ToHashSet();
        if (transaction.GetObject(
                database.GroupDictionaryId,
                OpenMode.ForRead) is not DBDictionary dictionary)
        {
            return 0;
        }

        var detached = 0;
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (transaction.GetObject(entry.Value, OpenMode.ForRead, false) is not Group group)
            {
                continue;
            }

            var members = group.GetAllEntityIds();
            var removals = members.Where(targets.Contains).Distinct().ToArray();
            if (removals.Length == 0 ||
                !TryFindCanonicalRoofOwner(
                    database,
                    transaction,
                    entry.Key,
                    members,
                    out _))
            {
                continue;
            }

            group.UpgradeOpen();
            foreach (var removal in removals)
            {
                group.Remove(removal);
                detached++;
            }
        }

        return detached;
    }

    private static bool TryFindCanonicalRoofOwner(
        Database database,
        Transaction transaction,
        string groupName,
        IReadOnlyCollection<ObjectId> memberIds,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        foreach (var memberId in memberIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    memberId,
                    OpenMode.ForRead,
                    out var owner,
                    database) ||
                owner is null ||
                owner.IsErased ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                continue;
            }

            if (!string.Equals(
                    groupName,
                    RoofDisplayGroupService.BuildCanonicalGroupName(transaction, memberId),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ownerId = memberId;
            return true;
        }

        return false;
    }

    private static void Trace(
        Document document,
        string? globalCommandName,
        RoofForeignClipboardDegradationResult result)
    {
#if DEBUG
        var message =
            "ROOF_FOREIGN_CLIPBOARD_DEGRADE " +
            $"timber={result.TimberCount} annotations={result.AnnotationCount} " +
            $"metadataCleared={Lower(result.Success && !result.FailClosed)} " +
            $"groupDetached={result.GroupDetachedCount} " +
            $"result={result.DiagnosticResult}";
        document.Editor.WriteMessage("\n" + message);
        Diagnostics.AcKrovyDiagnostics.Info(
            "RoofForeignClipboardDegrade",
            message,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName));
#endif
    }

#if DEBUG
    private static string Lower(bool value) => value ? "true" : "false";
#endif
}

internal sealed record RoofForeignClipboardDegradationResult(
    bool Success,
    bool FailClosed,
    int TimberCount,
    int AnnotationCount,
    int GroupDetachedCount,
    string DiagnosticResult);
