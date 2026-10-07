using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Command-start package capture and command-end Ordinary GRIP first claim.</summary>
internal static class RoofOrdinaryGripLifecycleService
{
    internal sealed record EntitySnapshot(ObjectId Id, Entity Copy);
    internal sealed record Member(bool Independent, ObjectId OwnerId, Line PlanCopy,
        ObjectId LineId, ObjectId SolidId, string ElementId, IReadOnlyList<EntitySnapshot> Entities,
        RoofOrdinaryPhysicalBuildState? BuildState, IReadOnlyList<Point3d> SolidVertices,
        TimberElementSignature Signature)
    {
        internal RoofOrdinaryPhysicalBuildStateTrace? BuildStateTrace { get; init; }
        internal bool BuildStateMigrated { get; init; }
    }
    internal sealed record Owner(ObjectId Id, Polyline Copy, ObjectId GroupId, ObjectId[] GroupMembers);
    internal sealed class Snapshot : IDisposable
    {
        private readonly Guid _commandId = Guid.NewGuid();
        private readonly HashSet<ObjectId> _candidates = new();
        private readonly HashSet<ObjectId> _processed = new();
        internal HashSet<ObjectId> ClaimedIds { get; } = new();
        // Shared native split evidence for TRIM and BREAK; kept per snapshot/command.
        internal HashSet<ObjectId> NativeTrimAppendedIds { get; } = new();
        internal bool ProcessingStarted { get; set; }
        internal int RotateAutoCount { get; set; }
        internal int RotateIndependentCount { get; set; }
        internal int RotatePhysicalOnlyCount { get; set; }
        internal int RotateMixedCount { get; set; }
        internal int LengthenAutoCount { get; set; }
        internal int LengthenIndependentCount { get; set; }
        internal int LengthenMixedCount { get; set; }
        private Document? _document;
        private string? _command;
        public List<Member> Members { get; } = new();
        public Dictionary<ObjectId, Owner> Owners { get; } = new();
        internal void BeginCommand(Document document, string? command)
        {
            _document = document;
            _command = command;
            TraceCommandState("begin");
        }
        internal void AddCandidates(IEnumerable<Member> members) =>
            _candidates.UnionWith(members.Select(member => member.LineId));
        internal void MarkProcessed(IEnumerable<Member> members) =>
            _processed.UnionWith(members.Select(member => member.LineId));
        internal void TraceCommandState(string phase)
        {
#if DEBUG
            var diagnostic = RoofGeneratedMemberEditCommandRules.IsRotateCommand(_command)
                ? "ROOF_ORDINARY_ROTATE_COMMAND_STATE"
                : RoofGeneratedMemberEditCommandRules.IsLengthenCommand(_command)
                ? "ROOF_ORDINARY_LENGTHEN_COMMAND_STATE"
                : RoofGeneratedMemberEditCommandRules.IsBreakCommand(_command)
                ? "ROOF_ORDINARY_BREAK_COMMAND_STATE"
                : RoofGeneratedMemberEditCommandRules.IsExtendCommand(_command)
                ? "ROOF_ORDINARY_EXTEND_COMMAND_STATE"
                : RoofGeneratedMemberEditCommandRules.IsTrimCommand(_command)
                ? "ROOF_ORDINARY_TRIM_COMMAND_STATE"
                : RoofGeneratedMemberEditCommandRules.IsClassicStretch(_command)
                    ? "ROOF_ORDINARY_STRETCH_COMMAND_STATE" : null;
            if (_document is not null && diagnostic is not null)
                _document.Editor.WriteMessage($"\n{diagnostic} commandId={_commandId:N} phase={phase} " +
                    $"candidateCount={_candidates.Count} processedCount={_processed.Count} appendedCount={NativeTrimAppendedIds.Count} " +
                    (RoofGeneratedMemberEditCommandRules.IsRotateCommand(_command)
                        ? $"autoCount={RotateAutoCount} independentCount={RotateIndependentCount} " +
                          $"physicalOnlyCount={RotatePhysicalOnlyCount} mixedCount={RotateMixedCount} "
                        : RoofGeneratedMemberEditCommandRules.IsLengthenCommand(_command)
                          ? $"autoCount={LengthenAutoCount} independentCount={LengthenIndependentCount} " +
                            $"mixedCount={LengthenMixedCount} " : string.Empty) +
                    "pendingRollbackCount=0 pendingRefreshCount=0 result=command-state-verified");
#endif
        }
        public void Dispose()
        {
            foreach (var member in Members)
                foreach (var entity in member.Entities) entity.Copy.Dispose();
            foreach (var owner in Owners.Values) owner.Copy.Dispose();
            Members.Clear();
            Owners.Clear();
            _candidates.Clear();
            _processed.Clear();
            ClaimedIds.Clear();
            NativeTrimAppendedIds.Clear();
            ProcessingStarted = false;
            RotateAutoCount = RotateIndependentCount = RotatePhysicalOnlyCount = RotateMixedCount = 0;
            LengthenAutoCount = LengthenIndependentCount = LengthenMixedCount = 0;
            // Rollback transactions finish synchronously. Deferred graphics work is
            // transferred by value to the refresh service, never retained here.
            TraceCommandState("disposed");
            _document = null;
            _command = null;
        }
    }

    public static Snapshot Capture(Document document)
    {
        var snapshot = new Snapshot();
        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(document.Database), OpenMode.ForRead);
            var entities = model.Cast<ObjectId>().Where(id => !id.IsErased)
                .Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<Entity>()
                .Where(e => !e.IsErased).ToArray();
            var metadata = new AutoCadTimberElementMetadataStore(transaction);
            var roundingStep = TimberElementDefaultProfileStore.Load().GetCuttingLengthRoundingStepMm();
            foreach (var line in entities.OfType<Line>())
            {
                var generated = RoofGeneratedTimberStore.Read(line).Data;
                var independent = RoofIndependentOrdinaryTimberStore.Read(line);
                if (RoofAttachedManualTimberStore.Read(line).Data is not null ||
                    !(independent is { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } && generated is null ||
                      independent is null && generated is { MemberKind: RoofGeneratedTimberKind.Rafter }) ||
                    !metadata.TryRead(line, out var timber) || timber is null) continue;
                var ownerRef = generated?.RoofOwnerReference ?? independent?.SourceRoofReference;
                var ownerId = ResolveOwner(document.Database, transaction, ownerRef);
                var owner = ownerId.IsNull ? null : (Polyline)transaction.GetObject(ownerId, OpenMode.ForRead);
                var key = generated is not null ? RoofGeneratedMemberKey.From(generated) : independent?.SourceGeneratedMemberKey;
                string? physicalKey = null;
                if (generated is not null && !RoofOrdinaryRafterSolidMaterializationService.TryGetPlanPhysicalIdentity(
                        line, generated.RoofOwnerReference, out physicalKey)) continue;
                var solids = entities.OfType<Solid3d>().Where(s => independent is not null
                    ? RoofIndependentOrdinaryTimberStore.Read(s) is { EntityRole: RoofIndependentOrdinaryEntityRole.PhysicalSolid } i &&
                      i.IndependentMemberId == independent.IndependentMemberId
                    : RoofPhysical3DGeneratedStore.Read(s).Data is { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } p &&
                      p.RoofOwnerReference == ownerRef && p.StructuralId == physicalKey).ToArray();
                if (solids.Length > 1) throw new InvalidOperationException("Duplicate Ordinary GRIP physical identity.");
                var annotations = entities.Where(e => RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(e, out var source) &&
                    string.Equals(source, line.Handle.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
#if DEBUG
                var trace = new RoofOrdinaryPhysicalBuildStateTrace();
#else
                RoofOrdinaryPhysicalBuildStateTrace? trace = null;
#endif
                trace?.Add("ownerReference", ownerRef);
                trace?.Add("ownerResolved", owner is not null);
                trace?.Add("independentMemberId", independent?.IndependentMemberId);
                trace?.Add("independentOrigin", independent?.OriginKind);
                trace?.Add("independentSchema", independent?.SchemaVersion);
                trace?.Add("sourceGeneratedKey", key);
                trace?.Add("provenancePresent", key.HasValue);
                trace?.Add("pairedPhysicalCount", solids.Length);
                trace?.Add("section", FormattableString.Invariant($"{timber.WidthMm:R}x{timber.HeightMm:R}"));
                trace?.Add("material", timber.Material);
                trace?.Add("commandStartPlan", RoofOrdinaryPhysicalBuildStateTrace.Axis(Axis(line)));
                foreach (var field in new[] { "definitionResolved", "recipeResolved", "elevationResolved",
                    "faceResolved", "physicalContextResolved", "provenanceResolved", "sectionFrameResolved" })
                    trace?.Add(field, "not_attempted");
                var migrated = false;
                var state = RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction, trace);
                if (state is not null)
                {
                    trace?.Add("buildStateSource", "persistent_member_xrecord");
                    var resolved = RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, Axis(line), out var rebased);
                    trace?.Rebase("storedRebase", state, Axis(line), resolved);
                    state = resolved ? rebased : null;
                }
                else if (independent is not null)
                    migrated = RoofIndependentOrdinaryPhysicalStateService.TryMigrate(document.Database, transaction,
                        line, solids.SingleOrDefault(), owner, timber, out state, trace);
                else if (owner is not null && key.HasValue)
                {
                    trace?.Add("buildStateSource", "live_roof_provenance");
                    var resolved = RoofOrdinaryRafterSolidMaterializationService.TryCaptureOrdinaryBuildState(
                        document.Database, transaction, owner, key.Value, Axis(line), out state,
                        historicalIndependent: independent is not null, trace: trace);
                    trace?.Add("provenanceResolved", resolved);
                    trace?.State(state);
                }
                else
                {
                    trace?.Add("buildStateSource", "unavailable");
                    trace?.Fail("RoofOrdinaryPhysicalBuildStateStore.Read:unavailable_and_" +
                        (owner is null ? "roof_owner_unresolved" : "provenance_key_missing"));
                }
                if (state is not null)
                    state = state with { WidthMm = timber.WidthMm, HeightMm = timber.HeightMm };
                // Geometry input only: retain the actual command-start section
                // before native GRIP can modify the derived physical body.
                if (state is not null && solids.Length == 1)
                {
                    var resolved = RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solids[0], state, out var framed, trace);
                    trace?.Add("sectionFrameResolved", resolved);
                    state = resolved ? framed : null;
                }
                trace?.Add("fullBuildStateResolved", state is not null);
                var package = new Entity[] { line }.Concat(solids).Concat(annotations).DistinctBy(e => e.ObjectId)
                    .Select(e => new EntitySnapshot(e.ObjectId, (Entity)e.Clone())).ToArray();
                snapshot.Members.Add(new Member(independent is not null, ownerId,
                    (Line)package[0].Copy, line.ObjectId, solids.FirstOrDefault()?.ObjectId ?? ObjectId.Null,
                    timber.ElementId, package, state, solids.Length == 0 ? Array.Empty<Point3d>() : ReadSolidVertices(solids[0]),
                    TimberElementSignature.FromMeasurement(TimberCalculator.Measure(timber, line.Length, roundingStep)))
                    { BuildStateTrace = trace, BuildStateMigrated = migrated });
                if (owner is not null && !snapshot.Owners.ContainsKey(ownerId))
                {
                    var hasGroup = RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database, transaction,
                        ownerId, OpenMode.ForRead, out var group) && group is not null;
                    snapshot.Owners.Add(ownerId, new Owner(ownerId, (Polyline)owner.Clone(),
                        hasGroup ? group!.ObjectId : ObjectId.Null, hasGroup ? group!.GetAllEntityIds() : Array.Empty<ObjectId>()));
                }
            }
            return snapshot;
        }
        catch (Exception ex)
        {
            snapshot.Dispose();
            document.Editor.WriteMessage("\nROOF_ORDINARY_GRIP_LIFECYCLE stage=capture result=failed reason=" + ex.GetType().Name);
            return snapshot;
        }
    }

    public static IReadOnlyCollection<ObjectId> Process(Document document, Snapshot? snapshot,
        string? command, IReadOnlyCollection<ObjectId> modifiedIds)
    {
        if (!RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command) || snapshot is null)
            return Array.Empty<ObjectId>();
        if (RoofGeneratedMemberEditCommandRules.IsRotateCommand(command))
            return RoofOrdinaryRotateLifecycleService.Process(document, snapshot, modifiedIds);
        if (RoofGeneratedMemberEditCommandRules.IsLengthenCommand(command))
            return RoofOrdinaryLengthenLifecycleService.Process(document, snapshot, modifiedIds);
        if (snapshot.ProcessingStarted) return snapshot.ClaimedIds;
        snapshot.ProcessingStarted = true;
        var claimed = snapshot.ClaimedIds;
        var selectionBeforeRollback = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        var physicalRejected = false;
        foreach (var ownerMembers in snapshot.Members.GroupBy(m => m.OwnerId))
        {
            var physicalRollbackMembers = new List<Member>();
            var affected = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
            var splits = new Dictionary<ObjectId, ObjectId[]>();
            using (var probe = document.Database.TransactionManager.StartTransaction())
            {
                if (snapshot.Owners.TryGetValue(ownerMembers.Key, out var baseline) &&
                    !SourceMatches((Polyline)probe.GetObject(baseline.Id, OpenMode.ForRead), baseline.Copy)) continue;
                foreach (var member in ownerMembers)
                {
                    var appended = RoofGeneratedMemberEditCommandRules.IsSplitCommand(command)
                        ? FindSplitLines(probe, snapshot, member) : Array.Empty<ObjectId>();
                    if (!member.Entities.Any(e => modifiedIds.Contains(e.Id)) && appended.Length == 0) continue;
                    var line = (Line)probe.GetObject(member.LineId, OpenMode.ForRead);
                    var plan = RoofOrdinaryGripLifecycleRules.Plan(Axis(line));
                    var change = RoofOrdinaryGripLifecycleRules.Classify(Axis(member.PlanCopy), plan);
                    // Direct physical edits must be rejected even if their centroid/volume do not change.
                    if (change != RoofOrdinaryGripChange.None || appended.Length > 0 || modifiedIds.Contains(member.SolidId) ||
                        line.StartPoint.Z != 0 || line.EndPoint.Z != 0)
                        affected.Add((member, change, plan));
                    if (appended.Length > 0) splits.Add(member.LineId, appended);
                }
            }
            if (affected.Count == 0) continue;
            snapshot.AddCandidates(affected.Select(item => item.Member));
            var autoPlan = affected.Where(a => !a.Member.Independent && a.Change != RoofOrdinaryGripChange.None).ToArray();
            var decision = "NONE";
            var accepted = true;
            try
            {
                using (var probe = document.Database.TransactionManager.StartTransaction())
                    foreach (var item in affected.Where(item => splits.ContainsKey(item.Member.LineId)))
                        if (!RoofOrdinaryTrimSplitRules.IsSplit(Axis(item.Member.PlanCopy),
                                new[] { item.Plan }.Concat(splits[item.Member.LineId].Select(id =>
                                    Axis((Line)probe.GetObject(id, OpenMode.ForRead)))).ToArray(),
                                allowTouchingPieces: RoofGeneratedMemberEditCommandRules.IsBreakCommand(command)))
                            throw new InvalidOperationException("Ordinary native split lineage geometry invalid.");
                if (autoPlan.Length > 0)
                {
                    using var probe = document.Database.TransactionManager.StartTransaction();
                    var owner = (Polyline)probe.GetObject(ownerMembers.Key, OpenMode.ForRead);
                    var unlocked = RoofDefinitionStore.Read(owner).Data?.EditState == RoofEditState.Unlocked;
                    accepted = unlocked && ConfirmDetach();
                    decision = accepted ? "YES" : "NO";
                }
                IReadOnlyDictionary<ObjectId, TimberElementData>? designations = null;
                using (document.LockDocument())
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    if (!accepted)
                    {
                        EraseSplitLines(transaction, splits.Values.SelectMany(ids => ids));
                        // Restore every affected package and the exact command-start GROUP in one transaction.
                        foreach (var item in affected) Restore(transaction, item.Member);
                        RestoreGroup(transaction, snapshot, ownerMembers.Key);
                    }
                    else
                    {
                        // Claim every native clone before retained-source detach invokes GROUP sync.
                        foreach (var item in affected.Where(item => splits.ContainsKey(item.Member.LineId)))
                            foreach (var id in splits[item.Member.LineId])
                                ClaimSplitLine(transaction, item.Member, (Line)transaction.GetObject(id, OpenMode.ForWrite));
                        var fragments = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
                        foreach (var item in affected)
                        {
                            if (item.Change == RoofOrdinaryGripChange.None)
                            {
                                Restore(transaction, item.Member);
                                if (modifiedIds.Contains(item.Member.SolidId))
                                {
                                    physicalRejected = true;
                                    physicalRollbackMembers.Add(item.Member);
                                }
                                continue;
                            }
                            Accept(document, transaction, item.Member, item.Plan);
                            if (splits.TryGetValue(item.Member.LineId, out var appended))
                                foreach (var id in appended)
                                {
                                    var fragment = CreateSplitMember(transaction, item.Member, id);
                                    snapshot.Members.Add(fragment);
                                    var plan = Axis((Line)transaction.GetObject(id, OpenMode.ForRead));
                                    Accept(document, transaction, fragment, plan);
                                    fragments.Add((fragment, RoofOrdinaryGripChange.End, plan));
                                }
                        }
                        designations = RecalculateDesignations(document, transaction,
                            affected.Concat(fragments).Where(item => item.Change != RoofOrdinaryGripChange.None)
                                .Select(item => item.Member).ToArray());
                        Verify(document, transaction, snapshot, affected.Concat(fragments).ToArray(), true, designations);
                        VerifySplitPackages(document, transaction, affected, splits);
                    }
                    Verify(document, transaction, snapshot, affected, accepted, designations);
                    transaction.Commit();
                }
                Terminal(ownerMembers.Key);
                if (!accepted)
                    RoofOrdinaryGripRollbackRefreshService.Schedule(
                        document,
                        selectionBeforeRollback,
                        affected.SelectMany(item => item.Member.Entities.Select(entity => entity.Id)).Distinct().ToArray(),
                        affected.Select(item => item.Member.LineId).ToArray());
                else if (physicalRollbackMembers.Count > 0)
                    RoofOrdinaryGripRollbackRefreshService.SchedulePhysical3D(
                        document,
                        selectionBeforeRollback,
                        physicalRollbackMembers.Select(item => item.SolidId)
                            .Where(id => !id.IsNull).Distinct().ToArray(),
                        physicalRollbackMembers.Select(item => item.LineId).Distinct().ToArray(),
                        physicalRollbackMembers.Select(item => item.SolidId)
                            .Where(id => !id.IsNull).Distinct().ToArray());
                foreach (var item in affected)
                    Trace(document, item.Member, item.Change, decision, accepted && !item.Member.Independent &&
                        item.Change != RoofOrdinaryGripChange.None, !accepted || item.Change == RoofOrdinaryGripChange.None,
                        accepted && item.Change != RoofOrdinaryGripChange.None, "pass", command,
                        designations?.TryGetValue(item.Member.LineId, out var assigned) == true &&
                        assigned.ElementId != item.Member.ElementId,
                        accepted && splits.TryGetValue(item.Member.LineId, out var appended) ? appended.Length + 1 : 1,
                        splits.ContainsKey(item.Member.LineId));
                TraceSplits(document, affected, splits, decision, accepted, "pass", command);
            }
            catch (Exception ex)
            {
                // A failed acceptance never creates overrides or invokes generic recovery.
                var restored = false;
                try
                {
                    using (document.LockDocument())
                    using (var transaction = document.Database.TransactionManager.StartTransaction())
                    {
                        EraseSplitLines(transaction, splits.Values.SelectMany(ids => ids));
                        foreach (var item in affected) Restore(transaction, item.Member);
                        RestoreGroup(transaction, snapshot, ownerMembers.Key);
                        Verify(document, transaction, snapshot, affected, accepted: false);
                        transaction.Commit();
                        restored = true;
                    }
                }
                catch (Exception rollbackError)
                {
                    document.Editor.WriteMessage("\nROOF_ORDINARY_GRIP_LIFECYCLE rollback=False result=failed reason=" + rollbackError.Message);
                }
                Terminal(ownerMembers.Key);
                foreach (var item in affected)
                    Trace(document, item.Member, item.Change, decision, false, restored, false,
                        "failed:" + ex.Message.Replace(' ', '_'), command);
                TraceSplits(document, affected, splits, decision, false, "failed:" + ex.Message.Replace(' ', '_'), command);
            }
            foreach (var item in affected) claimed.UnionWith(item.Member.Entities.Select(e => e.Id));
            claimed.UnionWith(splits.Values.SelectMany(ids => ids));
            snapshot.MarkProcessed(affected.Select(item => item.Member));
            // An unchanged owner may have been queued by native annotation/reference notifications.
            if (!ownerMembers.Key.IsNull) claimed.Add(ownerMembers.Key);
        }
        if (physicalRejected)
        {
            try { RoofPhysical3DWarningService.Show(); }
            catch (Exception ex) { document.Editor.WriteMessage("\nROOF_ORDINARY_GRIP_LIFECYCLE warning=failed reason=" + ex.GetType().Name); }
        }
        return claimed;
    }

    private static ObjectId[] FindSplitLines(Transaction transaction, Snapshot snapshot, Member source)
    {
        var generated = RoofGeneratedTimberStore.Read(source.PlanCopy).Data;
        var independent = RoofIndependentOrdinaryTimberStore.Read(source.PlanCopy);
        return snapshot.NativeTrimAppendedIds.Where(id => !id.IsErased &&
            transaction.GetObject(id, OpenMode.ForRead) is Line line &&
            (source.Independent
                ? RoofIndependentOrdinaryTimberStore.Read(line)?.IndependentMemberId == independent?.IndependentMemberId
                : RoofGeneratedTimberStore.Read(line).Data is { } clone && generated is not null &&
                  clone.RoofOwnerReference == generated.RoofOwnerReference &&
                  RoofGeneratedMemberKey.From(clone) == RoofGeneratedMemberKey.From(generated))).ToArray();
    }

    private static void ClaimSplitLine(Transaction transaction, Member source, Line line)
    {
        var inherited = RoofIndependentOrdinaryTimberStore.Read(source.PlanCopy);
        var generated = RoofGeneratedTimberStore.Read(source.PlanCopy).Data;
        var identity = inherited is not null
            ? inherited with { IndependentMemberId = Guid.NewGuid().ToString("N"), EntityRole = RoofIndependentOrdinaryEntityRole.PlanLine }
            : new RoofIndependentOrdinaryTimberData(RoofIndependentOrdinaryTimberDataSchema.CurrentVersion,
                Guid.NewGuid().ToString("N"), RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
                RoofIndependentOrdinaryEntityRole.PlanLine, generated!.RoofOwnerReference,
                RoofGeneratedMemberKey.From(generated));
        RoofIndependentOrdinaryTimberStore.TransferFromRoof(line, transaction, identity,
            RoofGeneratedTimberStore.RegAppName, RoofGeneratedTimberStore.LinkRegAppName);
    }

    private static Member CreateSplitMember(Transaction transaction, Member source, ObjectId lineId)
    {
        var line = (Line)transaction.GetObject(lineId, OpenMode.ForRead);
        var identity = RoofIndependentOrdinaryTimberStore.Read(line)!;
        var solidId = ObjectId.Null;
        if (!source.SolidId.IsNull)
        {
            var solid = (Solid3d)source.Entities.Single(entity => entity.Id == source.SolidId).Copy.Clone();
            var model = (BlockTableRecord)transaction.GetObject(line.OwnerId, OpenMode.ForWrite);
            solidId = model.AppendEntity(solid);
            transaction.AddNewlyCreatedDBObject(solid, true);
            RoofIndependentOrdinaryTimberStore.TransferFromRoof(solid, transaction,
                identity with { EntityRole = RoofIndependentOrdinaryEntityRole.PhysicalSolid },
                RoofPhysical3DGeneratedStore.RegAppName);
        }
        var copy = (Line)line.Clone();
        return source with { Independent = true, LineId = lineId, SolidId = solidId,
            PlanCopy = copy, Entities = new[] { new EntitySnapshot(lineId, copy) } };
    }

    private static void EraseSplitLines(Transaction transaction, IEnumerable<ObjectId> ids)
    {
        foreach (var id in ids.Distinct())
            if (!id.IsErased && transaction.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
    }

    private static void VerifySplitPackages(Document document, Transaction transaction,
        IReadOnlyList<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)> affected,
        IReadOnlyDictionary<ObjectId, ObjectId[]> splits)
    {
        var model = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(document.Database), OpenMode.ForRead);
        var entities = model.Cast<ObjectId>().Where(id => !id.IsErased)
            .Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
        foreach (var item in affected.Where(item => splits.ContainsKey(item.Member.LineId)))
        {
            var lines = new[] { item.Member.LineId }.Concat(splits[item.Member.LineId]).ToArray();
            var identities = lines.Select(id => RoofIndependentOrdinaryTimberStore.Read(
                (Entity)transaction.GetObject(id, OpenMode.ForRead))).ToArray();
            if (identities.Any(identity => identity is null) ||
                identities.Select(identity => identity!.IndependentMemberId).Distinct().Count() != lines.Length)
                throw new InvalidOperationException("Ordinary native split duplicate Independent identity.");
            foreach (var identity in identities)
                if (entities.OfType<Line>().Count(line => RoofIndependentOrdinaryTimberStore.Read(line) is
                        { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } data && data.IndependentMemberId == identity!.IndependentMemberId) != 1 ||
                    entities.OfType<Solid3d>().Count(solid => RoofIndependentOrdinaryTimberStore.Read(solid)?.IndependentMemberId == identity!.IndependentMemberId) !=
                        (item.Member.SolidId.IsNull ? 0 : 1))
                    throw new InvalidOperationException("Ordinary native split Line/Physical3D pairing invalid.");
            if (lines.Any(id => RoofGeneratedTimberStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data is not null))
                throw new InvalidOperationException("Ordinary native split retained active Generated identity.");
        }
    }

    public static void CancelExtend(Document document, Snapshot? snapshot, string? command)
    {
        if (snapshot is null || !RoofGeneratedMemberEditCommandRules.IsExtendCommand(command) || snapshot.ProcessingStarted) return;
        var affected = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
        var selection = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var member in snapshot.Members)
            {
                if (member.LineId.IsErased) continue;
                var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
                var change = RoofOrdinaryGripLifecycleRules.Classify(Axis(member.PlanCopy), Axis(line));
                if (change == RoofOrdinaryGripChange.None) continue;
                affected.Add((member, change, Axis(line)));
            }
            snapshot.AddCandidates(affected.Select(item => item.Member));
            foreach (var item in affected) Restore(transaction, item.Member);
            foreach (var ownerId in affected.Select(item => item.Member.OwnerId).Distinct())
                RestoreGroup(transaction, snapshot, ownerId);
            Verify(document, transaction, snapshot, affected, accepted: false);
            transaction.Commit();
        }
        snapshot.MarkProcessed(affected.Select(item => item.Member));
        if (affected.Count > 0)
            RoofOrdinaryGripRollbackRefreshService.Schedule(document, selection,
                affected.SelectMany(item => item.Member.Entities.Select(entity => entity.Id)).Distinct().ToArray(),
                affected.Select(item => item.Member.LineId).ToArray());
    }

    public static void CancelTrimSplit(Document document, Snapshot? snapshot, string? command)
    {
        if (snapshot is null || !RoofGeneratedMemberEditCommandRules.IsSplitCommand(command) || snapshot.ProcessingStarted) return;
        var affected = new List<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)>();
        var selection = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var member in snapshot.Members)
            {
                var appended = FindSplitLines(transaction, snapshot, member);
                if (member.LineId.IsErased) continue;
                var plan = Axis((Line)transaction.GetObject(member.LineId, OpenMode.ForRead));
                var change = RoofOrdinaryGripLifecycleRules.Classify(Axis(member.PlanCopy), plan);
                if (appended.Length == 0 && change == RoofOrdinaryGripChange.None) continue;
                affected.Add((member, change, plan));
                EraseSplitLines(transaction, appended);
                Restore(transaction, member);
                RestoreGroup(transaction, snapshot, member.OwnerId);
            }
            Verify(document, transaction, snapshot, affected, accepted: false);
            transaction.Commit();
        }
        snapshot.AddCandidates(affected.Select(item => item.Member));
        snapshot.MarkProcessed(affected.Select(item => item.Member));
        if (affected.Count > 0)
            RoofOrdinaryGripRollbackRefreshService.Schedule(document, selection,
                affected.SelectMany(item => item.Member.Entities.Select(entity => entity.Id)).Distinct().ToArray(),
                affected.Select(item => item.Member.LineId).ToArray());
    }

    private static void TraceSplits(Document document,
        IReadOnlyList<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)> affected,
        IReadOnlyDictionary<ObjectId, ObjectId[]> splits, string decision, bool accepted, string result, string? command)
    {
#if DEBUG
        using var transaction = document.Database.TransactionManager.StartTransaction();
        foreach (var item in affected.Where(item => splits.ContainsKey(item.Member.LineId)))
        {
            var appended = splits[item.Member.LineId];
            string Identity(ObjectId id) => id.IsErased ? "-" : RoofIndependentOrdinaryTimberStore.Read(
                (Entity)transaction.GetObject(id, OpenMode.ForRead))?.IndependentMemberId ?? "-";
            var diagnostic = RoofGeneratedMemberEditCommandRules.IsBreakCommand(command)
                ? "ROOF_ORDINARY_BREAK_SPLIT" : "ROOF_ORDINARY_TRIM_SPLIT";
            var newIds = accepted ? string.Join(",", appended.Select(Identity)) : "-";
            var message = $"{diagnostic} owner={item.Member.OwnerId.Handle} sourceLine={item.Member.LineId.Handle}" +
                $" retainedLine={item.Member.LineId.Handle} appendedLines={string.Join(",", appended.Select(id => id.Handle))}" +
                $" sourceState={(item.Member.Independent ? "Independent" : "AUTO")} splitDetected=True" +
                $" resultMemberCount={(accepted ? appended.Length + 1 : 1)} decision={decision}" +
                $" sourceIndependentMemberId={RoofIndependentOrdinaryTimberStore.Read(item.Member.PlanCopy)?.IndependentMemberId ?? "-"}" +
                $" retainedIndependentMemberId={Identity(item.Member.LineId)} newIndependentMemberIds={newIds}" +
                $" physicalRebuildCount={(accepted && !item.Member.SolidId.IsNull ? appended.Length + 1 : 0)}" +
                $" duplicateGeneratedIdentityCount={(result == "pass" ? "0" : "unconfirmed")} groupCanonical={(result == "pass" ? "True" : "unconfirmed")} result={result}";
            document.Editor.WriteMessage("\n" + message);
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info(diagnostic, message);
        }
#endif
    }

    private static void TraceBuildState(Document document, Member member, RoofSegment3D plan, string result, string failure)
    {
#if DEBUG
        const string diagnostic = "ROOF_ORDINARY_PHYSICAL_BUILD_STATE";
        var message = $"{diagnostic} line={member.LineId.Handle} solid={(member.SolidId.IsNull ? "none" : member.SolidId.Handle.ToString())}" +
            $" state={(member.Independent ? "Independent" : "AUTO")} elementId={member.ElementId}" +
            $" {member.BuildStateTrace?.Fields ?? "snapshotTrace=unavailable"}" +
            $" currentPlan={RoofOrdinaryPhysicalBuildStateTrace.Axis(plan)} failure={failure} scope=build-state-resolution result={result}";
        document.Editor.WriteMessage("\n" + message);
        AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info(diagnostic, message);
#endif
    }

    internal static void Accept(Document document, Transaction transaction, Member member, RoofSegment3D plan)
    {
        var buildState = member.BuildState;
        if (buildState is not null && RoofOrdinaryGripLifecycleRules.Classify(Axis(member.PlanCopy), plan) == RoofOrdinaryGripChange.Middle)
        {
            var resolved = RoofOrdinaryPhysicalBuildStateRules.TryRebase(buildState, plan, out var translated);
            member.BuildStateTrace?.Rebase("acceptedTranslationRebase", buildState, plan, resolved);
            buildState = resolved ? translated : null;
        }
        if (buildState is null)
        {
            TraceBuildState(document, member, plan, "failed", member.BuildStateTrace?.Failure ?? "command_snapshot_build_state_null");
            throw new InvalidOperationException("Ordinary GRIP full physical build state unavailable.");
        }

        StructuralMemberElevationState? elevation = null;
        var previousPlan = Axis(member.PlanCopy);
        if (member.Independent)
        {
            var lineForElevation = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
            elevation = StructuralMemberElevationStore.Read(lineForElevation, transaction);
            if (elevation is not null)
            {
                // Classic edits preserve slope magnitude + anchor Z — not AK_EDIT CalculationMode.
                if (!StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
                        elevation, previousPlan, plan, out var adapted) ||
                    adapted is null)
                    throw new InvalidOperationException("Ordinary GRIP elevation adaptation failed.");
                if (!StructuralMemberElevationRules.TryPatchBuildStateForElevation(
                        buildState, plan, adapted, out var patched) || patched is null)
                    throw new InvalidOperationException("Ordinary GRIP elevation build-state patch failed.");
                buildState = patched;
                elevation = adapted;
            }
        }

        if (!RoofOrdinaryPhysicalBuildStateRules.TryBuild(buildState, plan,
                out var physical, out var updated, out var reason, useIndependentHorizontalFrame: true) || physical is null || updated is null)
        {
            TraceBuildState(document, member, plan, "failed", "RoofOrdinaryPhysicalBuildStateRules.TryBuild:" + reason);
            throw new InvalidOperationException("Ordinary GRIP full physical builder failed: " + reason);
        }

        if (elevation is not null &&
            !StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(elevation, plan, updated))
            throw new InvalidOperationException("Ordinary GRIP elevation/physical pitch mismatch.");

        var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForWrite);
        line.StartPoint = Map(plan.Start);
        line.EndPoint = Map(plan.End);
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        if (!metadata.TryRead(line, out var timber) || timber?.ElementId != member.ElementId)
            throw new InvalidOperationException("Ordinary GRIP ElementId changed.");
        // Elevation-aware slope is authoritative when Elevation v1 exists.
        var planLength = plan.Start.DistanceTo(plan.End);
        var slope = elevation is not null
            ? StructuralMemberElevationRules.AbsolutePitchDegrees(
                StructuralMemberElevationRules.DeriveSlopeDegrees(
                    elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, planLength))
            : Math.Acos(Math.Clamp(planLength / physical.PhysicalLengthMm, 0d, 1d)) * 180d / Math.PI;
        var fallReversed = elevation is not null
            ? StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
                elevation.AxisStartElevationMm, elevation.AxisEndElevationMm)
            : timber.IsSlopeDirectionReversed;
        var updatedTimber = timber with
        {
            SlopeDegrees = slope,
            IsSlopeDirectionReversed = fallReversed,
        };
        metadata.Write(line, updatedTimber);
        if (!member.SolidId.IsNull)
        {
            var solid = (Solid3d)transaction.GetObject(member.SolidId, OpenMode.ForWrite);
            using var rebuilt = RoofOrdinaryRafterSolidMaterializationService.MaterializeOrdinaryMember(physical);
            using var xdata = solid.XData;
            var layer = solid.LayerId;
            var visible = solid.Visible;
            solid.CopyFrom(rebuilt);
            solid.LayerId = layer;
            solid.Visible = visible;
            if (xdata is not null) solid.XData = xdata;
            if (!SameVertices(ReadSolidVertices(solid), physical.SolidVertices.Select(Map).ToArray()))
                throw new InvalidOperationException("Ordinary GRIP materialized BRep differs from the Core physical member.");
            if (elevation is not null)
            {
                if (!RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, updated, out var captured) ||
                    captured is null ||
                    !StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(elevation, plan, captured))
                    throw new InvalidOperationException("Ordinary GRIP elevation/physical frame validation failed.");
                updated = captured;
            }
#if DEBUG
            RoofFinalSolidFrameAudit.TraceOrdinaryRebuild(document, line, solid, buildState, physical);
#endif
        }
        else if (!member.OwnerId.IsNull && RoofPhysicalElevationStore.Read(
                     (Polyline)transaction.GetObject(member.OwnerId, OpenMode.ForRead)).Data?.Physical3DEnabled == true)
            throw new InvalidOperationException("Ordinary GRIP expected physical package is missing.");
        if (!member.Independent)
        {
            var owner = (Polyline)transaction.GetObject(member.OwnerId, OpenMode.ForRead);
            var definition = RoofDefinitionStore.Read(owner).Data!;
            var overrides = new RoofManualOverrideSet(definition.Overrides).Remove(buildState.MemberKey);
            if (overrides.Items.Count != definition.Overrides.Count)
            {
                owner.UpgradeOpen();
                RoofDefinitionStore.Write(owner, transaction,
                    RoofGeneratedMemberOverrideRules.WithEditState(definition, definition.EditState, overrides.Items));
            }
            if (!RoofIndependentOrdinaryDetachService.TryDetach(document, transaction, owner, line,
                    false, new Vector3d(0, 0, 0), Array.Empty<ObjectId>(), updated))
                throw new InvalidOperationException("Ordinary GRIP detach failed.");
        }
        else
        {
            var profile = TimberElementDefaultProfileStore.Load();
            TimberAnnotationService.EnsureForElement(document.Database, transaction, line,
                updatedTimber,
                AutoCadAnnotationPresentationBatchContext.Create(document.Database, transaction, profile),
                member.ElementId, profile.GetCuttingLengthRoundingStepMm());
            var identity = RoofIndependentOrdinaryTimberStore.Read(line)!;
            foreach (var id in FindAnnotations(document.Database, transaction, line.Handle.ToString()))
                RoofIndependentOrdinaryTimberStore.Write((Entity)transaction.GetObject(id, OpenMode.ForWrite), transaction,
                    identity with { EntityRole = RoofIndependentOrdinaryEntityRole.Annotation });
            RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, updated);
            if (elevation is not null)
                StructuralMemberElevationStore.Write(line, transaction, elevation);
        }
        if (member.BuildStateMigrated) member.BuildStateTrace?.Add("migrationPersisted", true);
        TraceBuildState(document, member, plan, "pass", "none");
    }

    internal static IReadOnlyDictionary<ObjectId, TimberElementData> RecalculateDesignations(
        Document document, Transaction transaction, IReadOnlyList<Member> members)
    {
        if (members.Count == 0) return new Dictionary<ObjectId, TimberElementData>();
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        var profile = TimberElementDefaultProfileStore.Load();
        var roundingStep = profile.GetCuttingLengthRoundingStepMm();
        // All accepted bodies and ownership transfers are complete. Source roof slots
        // and provenance do not participate in manufacturing grouping.
        var sync = TimberElementItemIdentityService.SynchronizeElementIdsDetailed(
            document.Database, transaction, metadata, members.Select(member => member.LineId).ToArray(),
            roundingStep, members.ToDictionary(member => member.LineId, member => member.Signature));
        var presentation = AutoCadAnnotationPresentationBatchContext.Create(document.Database, transaction, profile);
        foreach (var id in members.Select(member => member.LineId).Concat(sync.WrittenIds).Distinct())
        {
            var entity = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
            if (!sync.DataById.TryGetValue(id, out var data) ||
                !metadata.TryRead(entity, out var persisted) || persisted?.ElementId != data.ElementId)
                throw new InvalidOperationException("Ordinary GRIP designation persistence failed.");
            TimberAnnotationService.EnsureForElement(document.Database, transaction, entity, data,
                presentation, sync.PreviousElementIdById[id], roundingStep);
            foreach (var annotationId in FindAnnotations(document.Database, transaction, entity.Handle.ToString()))
                if (ElementLabelStore.TryRead((Entity)transaction.GetObject(annotationId, OpenMode.ForRead), out var label) &&
                    label is not null && label.ElementId != data.ElementId)
                    throw new InvalidOperationException("Ordinary GRIP annotation designation is stale.");
            if (RoofIndependentOrdinaryTimberStore.Read(entity) is { } identity)
                foreach (var annotationId in FindAnnotations(document.Database, transaction, entity.Handle.ToString()))
                    RoofIndependentOrdinaryTimberStore.Write(
                        (Entity)transaction.GetObject(annotationId, OpenMode.ForWrite), transaction,
                        identity with { EntityRole = RoofIndependentOrdinaryEntityRole.Annotation });
        }
#if DEBUG
        foreach (var member in members)
        {
            var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
            if (!AutoCadEntityReader.TryReadTimberElement(line, metadata, out var current) || current is null)
                throw new InvalidOperationException("Ordinary GRIP designation measurement unavailable.");
            var measurement = TimberElementMeasurer.Measure(current, roundingStep);
            var message = $"ROOF_ORDINARY_DESIGNATION line={line.Handle}" +
                $" independentMemberId={RoofIndependentOrdinaryTimberStore.Read(line)?.IndependentMemberId}" +
                $" oldElementId={member.ElementId} newElementId={measurement.Data.ElementId}" +
                $" signatureChanged={member.Signature != TimberElementSignature.FromMeasurement(measurement)}" +
                $" oldCuttingLengthMm={member.Signature.CuttingLengthMm:R}" +
                $" planLengthMm={measurement.PlanLengthMm:R} trueLengthMm={measurement.ActualLengthMm:R}" +
                $" cuttingLengthMm={measurement.CuttingLengthMm:R}" +
                $" section={measurement.Data.WidthMm:R}x{measurement.Data.HeightMm:R} stage=preCommit result=pass";
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info("ROOF_ORDINARY_DESIGNATION", message);
            document.Editor.WriteMessage("\n" + message);
        }
#endif
        return sync.DataById;
    }

    internal static void Restore(Transaction transaction, Member member)
    {
        foreach (var saved in member.Entities)
        {
            var entity = (Entity)transaction.GetObject(saved.Id, OpenMode.ForWrite, openErased: true);
            if (entity.IsErased) entity.Erase(false);
            entity.CopyFrom(saved.Copy);
            using var xdata = saved.Copy.XData;
            if (xdata is not null) entity.XData = xdata;
        }
    }

    internal static void RestoreGroup(Transaction transaction, Snapshot snapshot, ObjectId ownerId)
    {
        if (!snapshot.Owners.TryGetValue(ownerId, out var owner) || owner.GroupId.IsNull) return;
        var group = (Group)transaction.GetObject(owner.GroupId, OpenMode.ForWrite);
        var actual = group.GetAllEntityIds();
        foreach (var id in actual.Except(owner.GroupMembers)) group.Remove(id);
        foreach (var id in owner.GroupMembers.Except(actual)) group.Append(id);
        if (!group.GetAllEntityIds().ToHashSet().SetEquals(owner.GroupMembers))
            throw new InvalidOperationException("Ordinary GRIP exact GROUP rollback failed.");
    }

    internal static void Verify(Document document, Transaction transaction, Snapshot snapshot,
        IReadOnlyList<(Member Member, RoofOrdinaryGripChange Change, RoofSegment3D Plan)> affected, bool accepted,
        IReadOnlyDictionary<ObjectId, TimberElementData>? designations = null,
        IReadOnlyCollection<ObjectId>? removedGroupIds = null, bool deferGroupVerification = false)
    {
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var item in affected)
        {
            var line = (Line)transaction.GetObject(item.Member.LineId, OpenMode.ForRead);
            var shouldAccept = accepted && item.Change != RoofOrdinaryGripChange.None;
            var expected = shouldAccept ? item.Plan : Axis(item.Member.PlanCopy);
            var expectedElementId = shouldAccept && designations is not null &&
                designations.TryGetValue(item.Member.LineId, out var assigned) ? assigned.ElementId : item.Member.ElementId;
            if (line.StartPoint.DistanceTo(Map(expected.Start)) > 0.0001d ||
                line.EndPoint.DistanceTo(Map(expected.End)) > 0.0001d ||
                !metadata.TryRead(line, out var timber) || timber?.ElementId != expectedElementId)
                throw new InvalidOperationException("Ordinary GRIP Plan/ElementId verification failed.");
            if (shouldAccept || item.Member.Independent)
            {
                var identity = RoofIndependentOrdinaryTimberStore.Read(line);
                if (identity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } ||
                    (item.Member.Independent && identity.IndependentMemberId !=
                        RoofIndependentOrdinaryTimberStore.Read(item.Member.PlanCopy)?.IndependentMemberId) ||
                    RoofGeneratedTimberStore.Read(line).Data is not null ||
                    (!item.Member.SolidId.IsNull && RoofIndependentOrdinaryTimberStore.Read(
                        (Entity)transaction.GetObject(item.Member.SolidId, OpenMode.ForRead))?.IndependentMemberId != identity.IndependentMemberId))
                    throw new InvalidOperationException("Ordinary GRIP independent ownership verification failed.");
                var package = new[] { item.Member.LineId, item.Member.SolidId }.Concat(
                    FindAnnotations(document.Database, transaction, line.Handle.ToString())).ToHashSet();
                if (snapshot.Owners.TryGetValue(item.Member.OwnerId, out var owner) && !owner.GroupId.IsNull &&
                    ((Group)transaction.GetObject(owner.GroupId, OpenMode.ForRead)).GetAllEntityIds().Any(package.Contains))
                    throw new InvalidOperationException("Independent Ordinary GRIP remains in roof GROUP.");
            }
            else if (RoofGeneratedTimberStore.Read(line).Data is null || RoofIndependentOrdinaryTimberStore.Read(line) is not null)
                throw new InvalidOperationException("Ordinary GRIP AUTO rollback ownership verification failed.");
            if (!shouldAccept)
            {
                foreach (var saved in item.Member.Entities)
                    if (!SameXData((Entity)transaction.GetObject(saved.Id, OpenMode.ForRead), saved.Copy))
                        throw new InvalidOperationException("Ordinary GRIP snapshot metadata verification failed.");
                if (!item.Member.SolidId.IsNull && !SameVertices(ReadSolidVertices(
                        (Solid3d)transaction.GetObject(item.Member.SolidId, OpenMode.ForRead)), item.Member.SolidVertices))
                    throw new InvalidOperationException("Ordinary GRIP physical snapshot verification failed.");
                var expectedAnnotations = item.Member.Entities.Where(e => e.Id != item.Member.LineId && e.Id != item.Member.SolidId)
                    .Select(e => e.Id).ToHashSet();
                if (!expectedAnnotations.SetEquals(FindAnnotations(document.Database, transaction, line.Handle.ToString())))
                    throw new InvalidOperationException("Ordinary GRIP annotation snapshot inventory verification failed.");
            }
        }
        if (deferGroupVerification) return;
        foreach (var ownerId in affected.Select(a => a.Member.OwnerId).Distinct())
        {
            if (!snapshot.Owners.TryGetValue(ownerId, out var owner) || owner.GroupId.IsNull) continue;
            var removed = accepted ? affected.Where(a => !a.Member.Independent && a.Change != RoofOrdinaryGripChange.None)
                .SelectMany(a => a.Member.Entities.Select(e => e.Id)).ToHashSet() : new HashSet<ObjectId>();
            var expected = owner.GroupMembers.Where(id => !removed.Contains(id) &&
                removedGroupIds?.Contains(id) != true).ToHashSet();
            var actual = ((Group)transaction.GetObject(owner.GroupId, OpenMode.ForRead)).GetAllEntityIds();
            if (actual.Length != expected.Count || !actual.ToHashSet().SetEquals(expected))
                throw new InvalidOperationException("Ordinary GRIP exact GROUP package verification failed.");
        }
    }

    private static bool ConfirmDetach()
    {
        return MemberWarningPreferenceService.ConfirmAutomaticDetach().Accepted;
    }

    private static void Terminal(ObjectId ownerId)
    {
        RoofCommandLifecycleTerminalState.MarkHandled(ownerId);
        if (!ownerId.IsNull) RoofCommandLifecycleTerminalState.Owners.Add(ownerId);
    }

    internal static bool SourceMatches(Polyline a, Polyline b) =>
        a.NumberOfVertices == b.NumberOfVertices && a.Closed == b.Closed &&
        Math.Abs(a.Elevation - b.Elevation) < 0.0001d && a.Normal.IsEqualTo(b.Normal) &&
        Enumerable.Range(0, a.NumberOfVertices).All(i => a.GetPoint2dAt(i).GetDistanceTo(b.GetPoint2dAt(i)) < 0.0001d &&
            Math.Abs(a.GetBulgeAt(i) - b.GetBulgeAt(i)) < 0.0001d);

    private static ObjectId ResolveOwner(Database database, Transaction transaction, string? handle)
    {
        if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return ObjectId.Null;
        try
        {
            var id = database.GetObjectId(false, new Handle(value), 0);
            return !id.IsErased && transaction.GetObject(id, OpenMode.ForRead) is Polyline ? id : ObjectId.Null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return ObjectId.Null; }
    }

    private static ObjectId[] FindAnnotations(Database database, Transaction transaction, string source)
    {
        var model = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
        return model.Cast<ObjectId>().Where(id => !id.IsErased && transaction.GetObject(id, OpenMode.ForRead) is Entity e &&
            RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(e, out var h) &&
            string.Equals(h, source, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static RoofSegment3D Axis(Line line) => new(new(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
        new(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
    private static Point3d Map(RoofPoint3D p) => new(p.X, p.Y, p.Z);

    internal static Point3d[] ReadSolidVertices(Solid3d solid)
    {
        var path = new FullSubentityPath(new[] { solid.ObjectId }, new SubentityId(SubentityType.Null, IntPtr.Zero));
        using var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep(path);
        return brep.Vertices.Select(v => v.Point).ToArray();
    }

    private static bool SameVertices(IReadOnlyList<Point3d> actual, IReadOnlyList<Point3d> expected) =>
        actual.Count > 0 && expected.Count > 0 &&
        actual.All(a => expected.Any(e => a.DistanceTo(e) < 0.001d)) &&
        expected.All(e => actual.Any(a => a.DistanceTo(e) < 0.001d));

    private static bool SameXData(Entity actual, Entity expected)
    {
        using var a = actual.XData;
        using var b = expected.XData;
        var av = a?.AsArray() ?? Array.Empty<TypedValue>();
        var bv = b?.AsArray() ?? Array.Empty<TypedValue>();
        return av.Length == bv.Length && av.Zip(bv).All(p =>
            p.First.TypeCode == p.Second.TypeCode && Equals(p.First.Value, p.Second.Value));
    }

    private static void Trace(Document document, Member member, RoofOrdinaryGripChange change,
        string decision, bool detached, bool rollback, bool rebuild, string result,
        string? command = null, bool designationChanged = false, int resultMemberCount = 1, bool splitDetected = false)
    {
        var diagnostic = RoofGeneratedMemberEditCommandRules.IsBreakCommand(command)
            ? "ROOF_ORDINARY_BREAK_LIFECYCLE"
            : RoofGeneratedMemberEditCommandRules.IsExtendCommand(command)
            ? "ROOF_ORDINARY_EXTEND_LIFECYCLE"
            : RoofGeneratedMemberEditCommandRules.IsTrimCommand(command)
            ? "ROOF_ORDINARY_TRIM_LIFECYCLE"
            : RoofGeneratedMemberEditCommandRules.IsClassicStretch(command)
                ? "ROOF_ORDINARY_STRETCH_LIFECYCLE" : "ROOF_ORDINARY_GRIP_LIFECYCLE";
        var message = $"{diagnostic} owner={(member.OwnerId.IsNull ? "-" : member.OwnerId.Handle.ToString())}" +
            $" line={member.LineId.Handle} solid={(member.SolidId.IsNull ? "-" : member.SolidId.Handle.ToString())}" +
            $" state={(member.Independent ? "Independent" : "AUTO")} endpoint={change} planChanged={change != RoofOrdinaryGripChange.None}" +
            $" decision={decision} detached={detached} rollback={rollback} physicalRebuild={rebuild}" +
            $" designationChanged={designationChanged} split={splitDetected || resultMemberCount > 1} resultMemberCount={resultMemberCount}" +
            $" manualOverrideWritten=false terminalHandled=true result={result}";
        AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info(diagnostic, message);
        document.Editor.WriteMessage("\n" + message);
    }
}
