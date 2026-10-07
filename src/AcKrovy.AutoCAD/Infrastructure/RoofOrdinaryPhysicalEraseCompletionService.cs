using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>First GROUP synchronization after restore commit, within the same ERASE undo scope.</summary>
internal static class RoofOrdinaryPhysicalEraseCompletionService
{
    internal static void Complete(Document document, IReadOnlyDictionary<ObjectId, ObjectId[]> owners,
        IReadOnlySet<ObjectId> restoredSolids, IReadOnlyList<string> lifecycleMessages)
    {
        var completion = new RoofPhysicalEraseCompletion();
        var syncCount = 0;
        void Synchronize(bool finalCheck)
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            foreach (var (ownerId, expected) in owners)
            {
                if (ownerId.IsErased || !RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database,
                        transaction, ownerId, OpenMode.ForRead, out var group) || group is null)
                    throw new InvalidOperationException("Physical-only ERASE canonical GROUP is unavailable.");
                var actual = group.GetAllEntityIds();
                var expectedSet = expected.ToHashSet();
                if (!RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expectedSet))
                {
                    if (finalCheck && (!actual.ToHashSet().SetEquals(expectedSet) ||
                            actual.GroupBy(id => id).Where(ids => ids.Count() > 1).Any(ids => !restoredSolids.Contains(ids.Key))))
                        throw new InvalidOperationException("Physical-only ERASE final GROUP mismatch exceeds restored-solid scope.");
                    // Use the established authoritative multiset canonicalizer only.
                    // Usually native unerase restored the slot at commit; no Append is needed.
                    if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, ownerId))
                        throw new InvalidOperationException("Physical-only ERASE GROUP synchronization failed.");
                    syncCount++;
                    actual = group.GetAllEntityIds();
                }
                if (!RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expectedSet))
                    throw new InvalidOperationException("Physical-only ERASE final GROUP verification failed.");
            }
            transaction.Commit();
        }
        if (!completion.TryComplete(document.Database.TransactionManager.TopTransaction is null,
                finalizeModel: () => Synchronize(false), warning: RoofPhysical3DWarningService.Show,
                verifyFinalGroup: () => Synchronize(true)))
            throw new InvalidOperationException("Physical-only ERASE completion requires a closed restore transaction.");
        foreach (var message in lifecycleMessages)
        {
            var final = message + $" phase=post-warning groupSyncCount={syncCount} restoreTransactionClosed=True";
            AcKrovyDiagnostics.Info("ROOF_ORDINARY_ERASE_LIFECYCLE", final);
            document.Editor.WriteMessage("\n" + final);
        }
    }
}
