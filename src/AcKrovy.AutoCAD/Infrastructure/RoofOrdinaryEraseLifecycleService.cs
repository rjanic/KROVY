using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Native ERASE first claim: delete a Plan-owned package or restore a derived-only erase.</summary>
internal static class RoofOrdinaryEraseLifecycleService
{
    internal sealed class Context : IDisposable
    {
        internal readonly RoofOrdinaryGripLifecycleService.Snapshot Packages;
        internal readonly HashSet<ObjectId> Handled = new();
        internal bool Processed;
        internal Action? PendingCompletion;
        internal Action? TakeCompletion() { var action = PendingCompletion; PendingCompletion = null; return action; }
        internal Context(Document document) => Packages = RoofOrdinaryGripLifecycleService.Capture(document);
        public void Dispose() { Packages.Dispose(); Handled.Clear(); Processed = false; PendingCompletion = null; }
    }

    internal static IReadOnlyCollection<ObjectId> Process(Document document, Context? context)
    {
        if (context is null || context.Processed) return context?.Handled ?? new HashSet<ObjectId>();
        context.Processed = true;
        var affected = context.Packages.Members.Where(member =>
            (member.Independent || member.OwnerId.IsNull || !member.OwnerId.IsErased) &&
            (member.LineId.IsErased || !member.SolidId.IsNull && member.SolidId.IsErased)).ToArray();
        if (affected.Length == 0) return context.Handled;
        // ERASE(false) can reattach the native GROUP slot only after transaction/command
        // unwind. Do not race that slot with an early EnsureGroup Append.
        var physicalOnlyCommand = affected.All(m => !m.LineId.IsErased);
        // Claim the complete package even if verification later rejects the native ERASE.
        // Legacy owner recovery must never process the same native event a second time.
        context.Handled.UnionWith(affected.SelectMany(m => m.Entities.Select(e => e.Id)));
        var externalErased = affected.Select(m => m.OwnerId).Distinct()
            .Where(context.Packages.Owners.ContainsKey)
            .SelectMany(id => context.Packages.Owners[id].GroupMembers)
            .Where(id => id.IsErased && !context.Handled.Contains(id)).ToHashSet();
        var deleted = new HashSet<ObjectId>();
        var rejected = false;
        var messages = new List<string>();
        using (document.LockDocument())
        try
        {
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var ownerId in affected.Where(m => !m.Independent && m.LineId.IsErased).Select(m => m.OwnerId).Distinct())
                RoofGeneratedRafterSetService.PreserveRecipe(document.Database, transaction, ownerId,
                    context.Packages.Members.Where(m => !m.Independent && m.OwnerId == ownerId).Select(m => m.PlanCopy));
            foreach (var member in affected)
            {
                var mixed = member.LineId.IsErased && !member.SolidId.IsNull && member.SolidId.IsErased;
                var action = RoofOrdinaryEraseRules.Decide(member.Independent, member.LineId.IsErased,
                    !member.SolidId.IsNull && member.SolidId.IsErased);
                var physicalOnly = action == RoofOrdinaryEraseAction.RejectPhysicalOnly;
                if (physicalOnly)
                {
                    RoofOrdinaryGripLifecycleService.Restore(transaction, member);
                    rejected = true;
                }
                else
                {
                    var ids = member.Entities.Select(e => e.Id).ToArray();
                    if (!member.OwnerId.IsNull && !member.OwnerId.IsErased)
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, member.OwnerId, ids);
                    foreach (var id in ids)
                    {
                        if (!id.IsErased && transaction.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
                        deleted.Add(id);
                    }
                }
                context.Handled.UnionWith(member.Entities.Select(e => e.Id));
#if DEBUG
                messages.Add($"ROOF_ORDINARY_ERASE_LIFECYCLE line={member.LineId.Handle} sourceState={(member.Independent ? "Independent" : "AUTO")} " +
                    $"plan2DAuthority=True mixedSelection={mixed} suppressionWritten=False independentCreated=False " +
                    $"attachedManualWritten=False legacyGeometryOverrideWritten=False physicalOnlyRejected={physicalOnly} " +
                    $"annotationsRemoved={(physicalOnly ? 0 : member.Entities.Count(e => e.Id != member.LineId && e.Id != member.SolidId))} " +
                    "groupCanonical=True terminalHandled=True result=pass");
#endif
            }
            foreach (var ownerId in affected.Where(_ => !physicalOnlyCommand).Select(m => m.OwnerId).Where(id => !id.IsNull && !id.IsErased).Distinct())
                if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, ownerId))
                    throw new InvalidOperationException("Ordinary ERASE canonical GROUP reconciliation failed.");
            if (deleted.Any(id => !id.IsErased)) throw new InvalidOperationException("Ordinary ERASE package remains live.");
            RoofOrdinaryGripLifecycleService.Verify(document, transaction, context.Packages,
                affected.Where(m => !deleted.Contains(m.LineId)).Select(m =>
                    (m, RoofOrdinaryGripChange.None, new AcKrovy.Core.Models.Roofs.RoofSegment3D(
                        new(m.PlanCopy.StartPoint.X, m.PlanCopy.StartPoint.Y, m.PlanCopy.StartPoint.Z),
                        new(m.PlanCopy.EndPoint.X, m.PlanCopy.EndPoint.Y, m.PlanCopy.EndPoint.Z)))).ToArray(),
                false, removedGroupIds: deleted.Concat(externalErased).ToArray(), deferGroupVerification: physicalOnlyCommand);
            foreach (var ownerId in affected.Where(_ => !physicalOnlyCommand).Select(m => m.OwnerId).Where(id => !id.IsNull && !id.IsErased).Distinct())
            {
                var saved = context.Packages.Owners[ownerId];
                if (saved.GroupId.IsNull) continue;
                var actual = ((Group)transaction.GetObject(saved.GroupId, OpenMode.ForRead)).GetAllEntityIds();
                var expected = saved.GroupMembers.Where(id => !deleted.Contains(id) && !externalErased.Contains(id)).ToHashSet();
                if (actual.Length != expected.Count || !actual.ToHashSet().SetEquals(expected))
                    throw new InvalidOperationException("Ordinary ERASE exact GROUP invariant failed.");
            }
            transaction.Commit();
        }
        if (physicalOnlyCommand)
        {
            var owners = affected.Select(m => m.OwnerId).Where(id => !id.IsNull && !id.IsErased).Distinct()
                .ToDictionary(id => id, id => context.Packages.Owners[id].GroupMembers.Where(member => !externalErased.Contains(member)).ToArray());
            var completedMessages = messages.ToArray();
            var solids = affected.Select(m => m.SolidId).Where(id => !id.IsNull).ToHashSet();
            context.PendingCompletion = () => RoofOrdinaryPhysicalEraseCompletionService.Complete(document, owners, solids, completedMessages);
            messages.Clear();
        }
        }
        catch (Exception exception)
        {
            messages.Clear();
            // The failed transaction has already discarded recipe/GROUP/plugin writes.
            // Restore only this command's affected Ordinary packages, retaining unrelated erasures.
            using var cleanup = document.Database.TransactionManager.StartTransaction();
            foreach (var member in affected) RoofOrdinaryGripLifecycleService.Restore(cleanup, member);
            foreach (var ownerId in affected.Select(m => m.OwnerId).Where(id => !id.IsNull && !id.IsErased).Distinct())
            {
                if (externalErased.Count == 0)
                    RoofOrdinaryGripLifecycleService.RestoreGroup(cleanup, context.Packages, ownerId);
                else if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, cleanup, ownerId))
                    throw new InvalidOperationException("Ordinary ERASE recovery GROUP reconciliation failed.", exception);
            }
            RoofOrdinaryGripLifecycleService.Verify(document, cleanup, context.Packages,
                affected.Select(m => (m, RoofOrdinaryGripChange.None, new AcKrovy.Core.Models.Roofs.RoofSegment3D(
                    new(m.PlanCopy.StartPoint.X, m.PlanCopy.StartPoint.Y, m.PlanCopy.StartPoint.Z),
                    new(m.PlanCopy.EndPoint.X, m.PlanCopy.EndPoint.Y, m.PlanCopy.EndPoint.Z)))).ToArray(),
                false, removedGroupIds: externalErased);
            cleanup.Commit();
            var failure = $"ROOF_ORDINARY_ERASE_LIFECYCLE suppressionWritten=False terminalHandled=True result=fail reason={exception.Message}";
            AcKrovyDiagnostics.Info("ROOF_ORDINARY_ERASE_LIFECYCLE", failure);
            document.Editor.WriteMessage("\n" + failure);
        }
        foreach (var member in affected.Where(m => !m.OwnerId.IsNull))
            if (RoofUnsupportedStretchRecoverySnapshotService.TryGet(member.OwnerId, out var snapshot))
                snapshot.ClaimOrdinary(context.Handled.Select(id => id.Handle.ToString()));
        foreach (var message in messages)
        {
            AcKrovyDiagnostics.Info("ROOF_ORDINARY_ERASE_LIFECYCLE", message);
            document.Editor.WriteMessage("\n" + message);
        }
        if (rejected && context.PendingCompletion is null) RoofPhysical3DWarningService.Show();
        return context.Handled;
    }
}
