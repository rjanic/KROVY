using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Member = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Member;
using Snapshot = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Snapshot;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Native LENGTHEN first claim. Final endpoint geometry, one decision, and the shared accepted package helpers.</summary>
internal static class RoofOrdinaryLengthenLifecycleService
{
    internal static IReadOnlyCollection<ObjectId> Process(Document document, Snapshot snapshot,
        IReadOnlyCollection<ObjectId> modifiedIds)
    {
        if (snapshot.ProcessingStarted) return snapshot.ClaimedIds;
        snapshot.ProcessingStarted = true;
        var affected = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
        var mixed = new HashSet<ObjectId>();
        var sourceChangedOwners = new HashSet<ObjectId>();
        using (var probe = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var owner in snapshot.Owners.Values)
                if (!RoofOrdinaryGripLifecycleService.SourceMatches(
                        (Polyline)probe.GetObject(owner.Id, OpenMode.ForRead), owner.Copy))
                    sourceChangedOwners.Add(owner.Id);
            foreach (var member in snapshot.Members)
            {
                // Whole-roof transformations keep the existing source-owned routing.
                if (!member.Independent && sourceChangedOwners.Contains(member.OwnerId)) continue;
                var line = (Line)probe.GetObject(member.LineId, OpenMode.ForRead);
                var plan = RoofOrdinaryGripLifecycleRules.Plan(Axis(line));
                var change = RoofOrdinaryLengthenRules.Endpoint(Axis(member.PlanCopy), plan);
                var solidChanged = modifiedIds.Contains(member.SolidId);
                // Final geometry and native modified IDs are evidence, not a presumed callback order.
                if (change == RoofOrdinaryGripChange.None)
                {
                    // Query/rejected native selection is a no-op, even if the host
                    // emits a notification. Consume only its package notifications
                    // so legacy unsupported-command recovery cannot rebuild it.
                    if (member.Entities.Any(entity => modifiedIds.Contains(entity.Id)))
                    {
                        snapshot.ClaimedIds.UnionWith(member.Entities.Select(entity => entity.Id));
                        if (!member.OwnerId.IsNull && !sourceChangedOwners.Contains(member.OwnerId))
                            snapshot.ClaimedIds.Add(member.OwnerId);
                    }
                    continue;
                }
                affected.Add((member, change, plan));
                if (change != RoofOrdinaryGripChange.None && solidChanged) mixed.Add(member.LineId);
            }
        }
        if (affected.Count == 0) return snapshot.ClaimedIds;
        snapshot.AddCandidates(affected.Select(item => item.Member));
        var plans = affected.Where(item => item.Change != RoofOrdinaryGripChange.None).ToArray();
        snapshot.LengthenAutoCount = plans.Count(item => !item.Member.Independent);
        snapshot.LengthenIndependentCount = plans.Count(item => item.Member.Independent);
        snapshot.LengthenMixedCount = mixed.Count;
        snapshot.TraceCommandState("collected");
        var selection = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        MemberWarningDecision? confirmation = null;
        var accepted = false;
        var restored = false;
        var failure = "pass";
        var messages = new List<string>();
        try
        {
            foreach (var item in plans)
                if (!RoofOrdinaryLengthenRules.IsEndpointLengthEdit(Axis(item.Member.PlanCopy), item.Plan, out var reason))
                    throw new InvalidOperationException("Ordinary LENGTHEN invalid final geometry:" + reason);
            var autos = plans.Where(item => !item.Member.Independent).ToArray();
            using (var probe = document.Database.TransactionManager.StartTransaction())
                if (autos.Any(item => item.Member.OwnerId.IsNull ||
                    RoofDefinitionStore.Read((Polyline)probe.GetObject(item.Member.OwnerId, OpenMode.ForRead))
                        .Data?.EditState != RoofEditState.Unlocked))
                    throw new InvalidOperationException("Ordinary LENGTHEN automatic roof is locked.");
            // The existing preference/dialog service owns UX and do-not-show-again semantics.
            if (autos.Length > 0) confirmation = MemberWarningPreferenceService.ConfirmAutomaticDetach();
            accepted = confirmation?.Accepted ?? true;
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                if (!accepted)
                    RestoreAll(transaction, snapshot, affected);
                else
                {
                    foreach (var item in affected)
                        RoofOrdinaryGripLifecycleService.Accept(document, transaction, item.Member, item.Plan);
                }
                var designations = accepted
                    ? RoofOrdinaryGripLifecycleService.RecalculateDesignations(document, transaction,
                        plans.Select(item => item.Member).ToArray()) : null;
                RoofOrdinaryGripLifecycleService.Verify(document, transaction, snapshot, affected, accepted, designations);
                if (accepted) RoofOrdinaryRotateLifecycleService.VerifyPackages(
                    document, transaction, plans.Select(item => item.Member).ToArray(), "LENGTHEN");
                foreach (var owner in affected.Select(item => item.Member.OwnerId).Where(id => !id.IsNull).Distinct())
                    if (!RoofAssemblyGroupSyncService.IsCurrent(document.Database, transaction, owner))
                        throw new InvalidOperationException("Ordinary LENGTHEN GROUP is not canonical.");
                foreach (var item in affected)
                    messages.Add(Message(transaction, item.Member, item.Plan, item.Change,
                        accepted, confirmation, "pass", groupCanonical: true));
                transaction.Commit();
                restored = !accepted;
            }
        }
        catch (Exception ex)
        {
            // Aborted acceptance leaves no identity, numbering or v2 writes. Restore
            // every native-mutated package, across owners, in one rollback transaction.
            accepted = false;
            failure = "failed:" + ex.Message.Replace(' ', '_');
            messages.Clear();
            try
            {
                using (document.LockDocument())
                using (var rollback = document.Database.TransactionManager.StartTransaction())
                {
                    RestoreAll(rollback, snapshot, affected);
                    RoofOrdinaryGripLifecycleService.Verify(document, rollback, snapshot, affected, false);
                    rollback.Commit();
                    restored = true;
                }
            }
            catch (Exception restoreError)
            {
                failure += ";rollback_failed:" + restoreError.Message.Replace(' ', '_');
            }
        }
        // First-claim is terminal even after a diagnosed failure: never fall into legacy edits.
        foreach (var item in affected)
            snapshot.ClaimedIds.UnionWith(item.Member.Entities.Select(entity => entity.Id));
        foreach (var owner in affected.Select(item => item.Member.OwnerId)
                     .Where(id => !id.IsNull && !sourceChangedOwners.Contains(id)).Distinct())
        {
            snapshot.ClaimedIds.Add(owner);
            RoofCommandLifecycleTerminalState.MarkHandled(owner);
            RoofCommandLifecycleTerminalState.Owners.Add(owner);
        }
        snapshot.MarkProcessed(affected.Select(item => item.Member));
        if (restored)
            RoofOrdinaryGripRollbackRefreshService.Schedule(document, selection,
                affected.SelectMany(item => item.Member.Entities.Select(entity => entity.Id)).Distinct().ToArray(),
                affected.Select(item => item.Member.LineId).ToArray());
        if (messages.Count == 0)
        {
            using var readback = document.Database.TransactionManager.StartTransaction();
            foreach (var item in affected)
                messages.Add(Message(readback, item.Member, item.Plan, item.Change, false, confirmation,
                    failure, groupCanonical: item.Member.OwnerId.IsNull ||
                        RoofAssemblyGroupSyncService.IsCurrent(document.Database, readback, item.Member.OwnerId),
                    rollbackVerified: restored));
        }
        foreach (var message in messages)
        {
            document.Editor.WriteMessage("\n" + message);
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info("ROOF_ORDINARY_LENGTHEN_LIFECYCLE", message);
        }
        snapshot.TraceCommandState("processed");
        return snapshot.ClaimedIds;
    }

    // Same safe abort policy as Ordinary EXTEND: restore retained native edits,
    // without prompting or guessing whether a cancelled session kept a partial edit.
    internal static void Cancel(Document document, Snapshot? snapshot)
    {
        if (snapshot is null || snapshot.ProcessingStarted) return;
        snapshot.ProcessingStarted = true;
        var affected = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
        var selection = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var member in snapshot.Members)
            {
                if (member.LineId.IsErased) continue;
                var plan = Axis((Line)transaction.GetObject(member.LineId, OpenMode.ForRead));
                var change = RoofOrdinaryLengthenRules.Endpoint(Axis(member.PlanCopy), plan);
                if (change != RoofOrdinaryGripChange.None) affected.Add((member, change, plan));
            }
            snapshot.AddCandidates(affected.Select(item => item.Member));
            RestoreAll(transaction, snapshot, affected);
            RoofOrdinaryGripLifecycleService.Verify(document, transaction, snapshot, affected, false);
            transaction.Commit();
        }
        snapshot.MarkProcessed(affected.Select(item => item.Member));
        if (affected.Count > 0)
            RoofOrdinaryGripRollbackRefreshService.Schedule(document, selection,
                affected.SelectMany(item => item.Member.Entities.Select(entity => entity.Id)).Distinct().ToArray(),
                affected.Select(item => item.Member.LineId).ToArray());
    }

    private static void RestoreAll(Transaction transaction, Snapshot snapshot,
        IEnumerable<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)> affected)
    {
        var items = affected.ToArray();
        foreach (var item in items) RoofOrdinaryGripLifecycleService.Restore(transaction, item.Member);
        foreach (var owner in items.Select(item => item.Member.OwnerId).Distinct())
            RoofOrdinaryGripLifecycleService.RestoreGroup(transaction, snapshot, owner);
    }

    private static string Message(Transaction transaction, Member member, RoofSegment3D plan, RoofOrdinaryGripChange change,
        bool accepted, MemberWarningDecision? confirmation, string result, bool groupCanonical, bool rollbackVerified = true)
    {
        var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
        var before = RoofIndependentOrdinaryTimberStore.Read(member.PlanCopy)?.IndependentMemberId ?? "none";
        var after = RoofIndependentOrdinaryTimberStore.Read(line)?.IndependentMemberId ?? "none";
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        _ = metadata.TryRead(line, out var timber);
        var changed = change != RoofOrdinaryGripChange.None;
        var decision = changed && !member.Independent && confirmation is not null
            ? (confirmation.Accepted ? "YES" : "NO") : "NONE";
        var source = member.BuildState is null ? "unavailable" : member.BuildStateMigrated ? "migrated_member_package" : member.Independent
            ? "persistent_member_xrecord" : "live_roof_provenance";
        return $"ROOF_ORDINARY_LENGTHEN_LIFECYCLE owner={(member.OwnerId.IsNull ? "none" : member.OwnerId.Handle.ToString())}" +
            $" line={line.Handle} solid={(member.SolidId.IsNull ? "none" : member.SolidId.Handle.ToString())}" +
            $" state={(member.Independent ? "Independent" : "AUTO")} planChanged={changed}" +
            $" endpoint={(change is RoofOrdinaryGripChange.Start or RoofOrdinaryGripChange.End ? change : RoofOrdinaryGripChange.None)}" +
            $" collinear={RoofOrdinaryLengthenRules.IsCollinear(Axis(member.PlanCopy), plan)} decision={decision}" +
            $" confirmationShown={changed && !member.Independent && confirmation?.ConfirmationShown == true}" +
            $" automaticConfirm={changed && !member.Independent && confirmation?.AutomaticConfirm == true}" +
            $" detached={accepted && changed && !member.Independent} rollback={(!accepted || !changed) && rollbackVerified}" +
            $" physicalRebuild={accepted && changed} independentMemberIdBefore={before} independentMemberIdAfter={after}" +
            $" elementIdBefore={member.ElementId} elementIdAfter={timber?.ElementId ?? "unavailable"}" +
            $" designationChanged={timber?.ElementId != member.ElementId} buildStateSource={source}" +
            $" suppressionWritten=False manualOverrideWritten=False attachedManualWritten=False groupCanonical={groupCanonical}" +
            $" terminalHandled=True result={result}";
    }

    private static RoofSegment3D Axis(Line line) => new(new(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
        new(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
}


