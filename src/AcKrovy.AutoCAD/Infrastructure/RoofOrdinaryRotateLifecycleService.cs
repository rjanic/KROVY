using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Member = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Member;
using Snapshot = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Snapshot;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Native ROTATE first claim. One command decision and one accepted package transaction.</summary>
internal static class RoofOrdinaryRotateLifecycleService
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
                var change = RoofOrdinaryGripLifecycleRules.Classify(Axis(member.PlanCopy), plan);
                var solidChanged = modifiedIds.Contains(member.SolidId);
                // Final geometry and native modified IDs are evidence, not a presumed callback order.
                if (change == RoofOrdinaryGripChange.None && !solidChanged &&
                    line.StartPoint.Z == 0 && line.EndPoint.Z == 0) continue;
                affected.Add((member, change, plan));
                if (change != RoofOrdinaryGripChange.None && solidChanged) mixed.Add(member.LineId);
            }
        }
        if (affected.Count == 0) return snapshot.ClaimedIds;
        snapshot.AddCandidates(affected.Select(item => item.Member));
        var plans = affected.Where(item => item.Change != RoofOrdinaryGripChange.None).ToArray();
        var physicalOnly = affected.Where(item => item.Change == RoofOrdinaryGripChange.None &&
            modifiedIds.Contains(item.Member.SolidId)).ToArray();
        snapshot.RotateAutoCount = plans.Count(item => !item.Member.Independent);
        snapshot.RotateIndependentCount = plans.Count(item => item.Member.Independent);
        snapshot.RotatePhysicalOnlyCount = physicalOnly.Length;
        snapshot.RotateMixedCount = mixed.Count;
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
                if (!RoofOrdinaryRotateRules.IsRigidPlanRotation(Axis(item.Member.PlanCopy), item.Plan))
                    throw new InvalidOperationException("Ordinary ROTATE is not a rigid planar rotation.");
            var autos = plans.Where(item => !item.Member.Independent).ToArray();
            using (var probe = document.Database.TransactionManager.StartTransaction())
                if (autos.Any(item => item.Member.OwnerId.IsNull ||
                    RoofDefinitionStore.Read((Polyline)probe.GetObject(item.Member.OwnerId, OpenMode.ForRead))
                        .Data?.EditState != RoofEditState.Unlocked))
                    throw new InvalidOperationException("Ordinary ROTATE automatic roof is locked.");
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
                        if (item.Change == RoofOrdinaryGripChange.None)
                            RoofOrdinaryGripLifecycleService.Restore(transaction, item.Member);
                        else
                            RoofOrdinaryGripLifecycleService.Accept(document, transaction, item.Member, item.Plan);
                }
                var designations = accepted
                    ? RoofOrdinaryGripLifecycleService.RecalculateDesignations(document, transaction,
                        plans.Select(item => item.Member).ToArray()) : null;
                RoofOrdinaryGripLifecycleService.Verify(document, transaction, snapshot, affected, accepted, designations);
                if (accepted) VerifyPackages(document, transaction, plans.Select(item => item.Member).ToArray());
                foreach (var owner in affected.Select(item => item.Member.OwnerId).Where(id => !id.IsNull).Distinct())
                    if (!RoofAssemblyGroupSyncService.IsCurrent(document.Database, transaction, owner))
                        throw new InvalidOperationException("Ordinary ROTATE GROUP is not canonical.");
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
        else if (accepted && physicalOnly.Length > 0)
            RoofOrdinaryGripRollbackRefreshService.SchedulePhysical3D(document, selection,
                physicalOnly.Select(item => item.Member.SolidId).ToArray(),
                physicalOnly.Select(item => item.Member.LineId).ToArray(),
                physicalOnly.Select(item => item.Member.SolidId).ToArray());
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
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info("ROOF_ORDINARY_ROTATE_LIFECYCLE", message);
        }
        snapshot.TraceCommandState("processed");
        // Warning is strictly post-commit UX. It cannot repair/append GROUP or accept 3D.
        if (physicalOnly.Length > 0)
            try { RoofPhysical3DWarningService.Show(); }
            catch (Exception ex) { document.Editor.WriteMessage("\nROOF_ORDINARY_ROTATE_WARNING result=failed reason=" + ex.GetType().Name); }
        return snapshot.ClaimedIds;
    }

    private static void RestoreAll(Transaction transaction, Snapshot snapshot,
        IEnumerable<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)> affected)
    {
        var items = affected.ToArray();
        foreach (var item in items) RoofOrdinaryGripLifecycleService.Restore(transaction, item.Member);
        foreach (var owner in items.Select(item => item.Member.OwnerId).Distinct())
            RoofOrdinaryGripLifecycleService.RestoreGroup(transaction, snapshot, owner);
    }

    // Shared read-only package invariant for command-wide native Plan edits.
    internal static void VerifyPackages(Document document, Transaction transaction, IReadOnlyList<Member> members,
        string command = "ROTATE")
    {
        var model = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(document.Database), OpenMode.ForRead);
        var entities = model.Cast<ObjectId>().Where(id => !id.IsErased)
            .Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
        var identities = new HashSet<string>();
        foreach (var member in members)
        {
            var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
            var identity = RoofIndependentOrdinaryTimberStore.Read(line)!;
            var state = RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction);
            var package = entities.Where(entity =>
                RoofIndependentOrdinaryTimberStore.Read(entity)?.IndependentMemberId == identity.IndependentMemberId).ToArray();
            if (!identities.Add(identity.IndependentMemberId) || package.OfType<Line>().Count() != 1 ||
                package.OfType<Solid3d>().Count() != (member.SolidId.IsNull ? 0 : 1) ||
                state?.Version != RoofOrdinaryPhysicalBuildState.CurrentVersion ||
                state.SectionFrame is null || state.AcceptedPlanAxis != Axis(line) ||
                package.Any(entity => RoofGeneratedTimberStore.Read(entity).Data is not null ||
                    RoofPhysical3DGeneratedStore.Read(entity).Data is not null || RoofAttachedManualTimberStore.Read(entity).Data is not null))
                throw new InvalidOperationException($"Ordinary {command} independent package uniqueness/state failed.");
        }
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
        var decision = changed && !member.Independent ? (confirmation?.Accepted == true ? "YES" : "NO") : "NONE";
        var source = member.BuildStateMigrated ? "migrated_member_package" : member.Independent
            ? "persistent_member_xrecord" : "live_roof_provenance";
        return $"ROOF_ORDINARY_ROTATE_LIFECYCLE owner={(member.OwnerId.IsNull ? "none" : member.OwnerId.Handle.ToString())}" +
            $" line={line.Handle} solid={(member.SolidId.IsNull ? "none" : member.SolidId.Handle.ToString())}" +
            $" state={(member.Independent ? "Independent" : "AUTO")} planChanged={changed}" +
            $" rotationDetected={RoofOrdinaryRotateRules.RotationDetected(Axis(member.PlanCopy), plan)} decision={decision}" +
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
