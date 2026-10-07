using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Member = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Member;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Shared native Ordinary clone first claim. COPY and MIRROR use one package engine.</summary>
internal static class RoofOrdinaryCopyLifecycleService
{
    internal sealed class Context : IDisposable
    {
        private readonly Document _document;
        private readonly Guid _commandId = Guid.NewGuid();
        internal readonly RoofOrdinaryGripLifecycleService.Snapshot Packages;
        internal readonly HashSet<ObjectId> Sources = new();
        internal readonly HashSet<ObjectId> LineClones = new();
        internal readonly HashSet<ObjectId> SolidClones = new();
        internal readonly HashSet<ObjectId> Claimed = new();
        internal readonly HashSet<ObjectId> Handled = new();
        internal readonly HashSet<ObjectId> Processed = new();
        internal readonly HashSet<ObjectId> Rejected = new();
        internal bool ProcessingStarted;
        internal bool Mirror { get; }
        internal bool? EraseSource { get; set; }
        internal string Operation => Mirror ? "MIRROR" : "COPY";

        internal Context(Document document, bool mirror = false)
        {
            _document = document;
            Mirror = mirror;
            Packages = RoofOrdinaryGripLifecycleService.Capture(document);
            Trace("begin");
        }

        internal void Observe(RoofNativeCloneSnapshot native)
        {
            foreach (var map in native.GetMappings())
            foreach (var member in Packages.Members)
            {
                var scoped = Scope(member, map, native);
                if (scoped.Count == 0) continue;
                Sources.Add(member.LineId);
                if (scoped.TryGetValue(member.LineId, out var line)) LineClones.Add(line);
                if (scoped.TryGetValue(member.SolidId, out var solid)) SolidClones.Add(solid);
            }
        }

        internal void Trace(string phase)
        {
#if DEBUG
            _document.Editor.WriteMessage($"\nROOF_ORDINARY_{Operation}_COMMAND_STATE commandId={_commandId:N} phase={phase} " +
                $"sourceCount={Sources.Count} lineCloneCount={LineClones.Count} solidCloneCount={SolidClones.Count} " +
                $"claimedCloneCount={Claimed.Count} processedCount={Processed.Count} rejectedPhysicalOnlyCount={Rejected.Count} " +
                $"eraseSource={EraseSource?.ToString() ?? "Unknown"} result=command-state-verified");
#endif
        }

        public void Dispose()
        {
            Packages.Dispose();
            Sources.Clear(); LineClones.Clear(); SolidClones.Clear(); Claimed.Clear(); Handled.Clear(); Processed.Clear(); Rejected.Clear();
            ProcessingStarted = false;
            EraseSource = null;
            Trace("disposed");
        }
    }

    private sealed record Candidate(Member Source, ObjectId Line, ObjectId NativeSolid, RoofSegment3D Plan,
        double NativeDz, bool PlanZNormalized, bool EraseSource = false);

    // Owner COPY keeps its established lifecycle even for an incomplete member subset.
    private static IReadOnlyDictionary<ObjectId, ObjectId> Scope(Member member,
        IReadOnlyDictionary<ObjectId, ObjectId> map, RoofNativeCloneSnapshot native) =>
        RoofOrdinaryCopyCloneRules.GetMappedPackage(member.Entities.Select(entity => entity.Id).ToArray(),
            map, native.PreExistingIds, !member.OwnerId.IsNull && map.ContainsKey(member.OwnerId));

    public static IReadOnlyCollection<ObjectId> Process(Document document, Context? context, RoofNativeCloneSnapshot native,
        IReadOnlyCollection<ObjectId>? mirrorModifiedIds = null)
    {
        if (context is null || context.ProcessingStarted) return context?.Handled ?? new HashSet<ObjectId>();
        context.ProcessingStarted = true;
        context.Observe(native);
        var sources = new HashSet<Member>();
        var nativeClones = new HashSet<ObjectId>();
        var candidates = new Dictionary<ObjectId, Candidate>();
        var copied = new List<Member>();
        var messages = Array.Empty<string>();
        var rejectedPhysicalOnly = false;
        var replaced = new HashSet<Member>();
        var removedSourceIds = new HashSet<ObjectId>();
        AcKrovy.Core.Services.MemberWarningDecision? mirrorDecision = null;
        using (document.LockDocument())
        {
            try
            {
                using var transaction = document.Database.TransactionManager.StartTransaction();
                var scopes = new List<(Member Source, IReadOnlyDictionary<ObjectId, ObjectId> Mapping)>();
                if (context.Mirror)
                    sources.UnionWith(context.Packages.Members.Where(source =>
                        source.Entities.Any(e => mirrorModifiedIds?.Contains(e.Id) == true || e.Id.IsErased) &&
                        (source.OwnerId.IsNull || !source.OwnerId.IsErased &&
                            mirrorModifiedIds?.Contains(source.OwnerId) != true &&
                            !native.IsSourceChanged((Polyline)transaction.GetObject(source.OwnerId, OpenMode.ForRead)) &&
                            !native.GetMappings().Any(map => map.ContainsKey(source.OwnerId)))));
                foreach (var map in native.GetMappings())
                foreach (var source in context.Packages.Members)
                {
                    var scoped = Scope(source, map, native).Where(pair => !native.IsMemberCloneConsumed(pair.Value) &&
                        !RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(pair.Value.Handle.ToString()))
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    if (scoped.Count == 0) continue;
                    sources.Add(source);
                    nativeClones.UnionWith(scoped.Values);
                    scopes.Add((source, scoped));
                }
                foreach (var (source, scoped) in scopes)
                {
                    if (!scoped.TryGetValue(source.LineId, out var cloneId) || cloneId.IsErased) continue;
                    var line = (Line)transaction.GetObject(cloneId, OpenMode.ForRead);
                    var plan = Geometry(line);
                    if (context.Mirror)
                    {
                        if (!RoofOrdinaryMirrorRules.TryResolve(Axis(source.PlanCopy), Axis(line), out _))
                            throw new InvalidOperationException("Ordinary MIRROR reflection is invalid or ambiguous.");
                        plan = new(RoofOrdinaryGripLifecycleRules.Plan(Axis(line)).Start,
                            RoofOrdinaryGripLifecycleRules.Plan(Axis(line)).End);
                    }
                    else if (!RoofOrdinaryCopyPlanRules.TryAccept(Geometry(source.PlanCopy), Geometry(line), out plan))
                        throw new InvalidOperationException("Ordinary COPY is not a rigid planar placement.");
                    candidates.TryAdd(cloneId, new(source, cloneId, scoped.GetValueOrDefault(source.SolidId),
                        new(plan.Start, plan.End), line.StartPoint.Z - source.PlanCopy.StartPoint.Z,
                        line.StartPoint.Z != 0 || line.EndPoint.Z != 0,
                        context.Mirror && RoofOrdinaryMirrorRules.ReplacesSource(false, source.LineId.IsErased)));
                }
                if (context.Mirror)
                {
                    // Proven HOST MIRROR Yes can transform the SAME Line without an
                    // appended/erased event. Inspect raw native changes before filtering.
                    foreach (var source in context.Packages.Members)
                    {
                        if (!source.OwnerId.IsNull && (source.OwnerId.IsErased ||
                            mirrorModifiedIds?.Contains(source.OwnerId) == true ||
                            native.IsSourceChanged((Polyline)transaction.GetObject(source.OwnerId, OpenMode.ForRead)) ||
                            native.GetMappings().Any(map => map.ContainsKey(source.OwnerId)))) continue;
                        if (candidates.Values.Any(c => c.Source.LineId == source.LineId)) continue;
                        if (!source.LineId.IsErased && mirrorModifiedIds?.Contains(source.LineId) == true)
                        {
                            var line = (Line)transaction.GetObject(source.LineId, OpenMode.ForRead);
                            if (mirrorModifiedIds.Contains(source.LineId))
                            {
                                sources.Add(source);
                                if (!RoofOrdinaryMirrorRules.TryResolve(Axis(source.PlanCopy), Axis(line), out _))
                                    throw new InvalidOperationException("In-place Ordinary MIRROR reflection is invalid or ambiguous.");
                                var plan = RoofOrdinaryGripLifecycleRules.Plan(Axis(line));
                                candidates.Add(source.LineId, new(source, source.LineId, ObjectId.Null, plan,
                                    line.StartPoint.Z - source.PlanCopy.StartPoint.Z,
                                    line.StartPoint.Z != 0 || line.EndPoint.Z != 0, true));
                                context.Sources.Add(source.LineId); context.LineClones.Add(source.LineId);
                                context.Claimed.Add(source.LineId);
                                continue;
                            }
                        }
                        if (!source.SolidId.IsNull && (source.SolidId.IsErased ||
                            mirrorModifiedIds?.Contains(source.SolidId) == true))
                        {
                            // Physical-only in-place MIRROR Yes is rejected as one
                            // derived package, including a natively erased body.
                            RoofOrdinaryGripLifecycleService.Restore(transaction, source);
                            sources.Add(source); context.Rejected.Add(source.SolidId);
                            context.Sources.Add(source.LineId); context.SolidClones.Add(source.SolidId);
                            context.Claimed.Add(source.SolidId);
                            rejectedPhysicalOnly = true;
                        }
                    }
                    context.EraseSource = candidates.Values.Any(c => c.EraseSource);
                    replaced.UnionWith(candidates.Values.Where(c => c.EraseSource).Select(c => c.Source));
                    removedSourceIds.UnionWith(replaced.SelectMany(s => s.Entities.Select(e => e.Id)));
                }
                // One operation-wide decision, before identity, suppression, rebuild or numbering.
                if (context.Mirror)
                {
                    var decision = mirrorDecision = MemberWarningPreferenceService.ConfirmAutomaticDetach(
                        candidates.Values.Any(candidate => !candidate.Source.Independent));
                    if (!decision.Accepted)
                    {
                        RollbackMirror(document, transaction, context, sources, nativeClones);
                        VerifyPackages(document, transaction, Array.Empty<Member>());
                        VerifyCanonicalGroups(document, transaction, context.Packages, sources, new HashSet<ObjectId>());
                        transaction.Commit();
                        foreach (var candidate in candidates.Values)
                        {
                            context.Processed.Add(candidate.Line);
                            var message = RejectedMirrorMessage(candidate, decision);
                            if (message.Length == 0) continue;
                            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info("ROOF_ORDINARY_MIRROR_LIFECYCLE", message);
                            document.Editor.WriteMessage("\n" + message);
                        }
                        return context.Handled;
                    }
                }
                context.Claimed.UnionWith(nativeClones);
                // No generated-key scan is allowed while native duplicate metadata survives.
                // Claim ALL lines first; discard ALL mapped derived clones before rebuilding.
                foreach (var candidate in candidates.Values)
                {
                    var line = (Line)transaction.GetObject(candidate.Line, OpenMode.ForWrite);
                    var old = RoofIndependentOrdinaryTimberStore.Read(candidate.Source.PlanCopy);
                    var generated = RoofGeneratedTimberStore.Read(candidate.Source.PlanCopy).Data;
                    var newId = Guid.NewGuid().ToString("N");
                    var identity = context.Mirror ? RoofOrdinaryMirrorRules.CreateIdentity(old, generated, newId) :
                        RoofOrdinaryCopyCloneRules.CreateIdentity(old, generated, newId);
                    RoofIndependentOrdinaryTimberStore.TransferFromRoof(line, transaction, identity,
                        RoofGeneratedTimberStore.RegAppName, RoofGeneratedTimberStore.LinkRegAppName);
                    line.StartPoint = Map(candidate.Plan.Start); line.EndPoint = Map(candidate.Plan.End);
                }
                foreach (var source in sources)
                {
                    var clones = nativeClones.Where(id => !candidates.ContainsKey(id)).ToArray();
                    if (!source.OwnerId.IsNull)
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, source.OwnerId, clones);
                }
                foreach (var id in nativeClones.Where(id => !candidates.ContainsKey(id)))
                    if (!id.IsErased && transaction.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
                foreach (var solid in context.SolidClones.Where(id => nativeClones.Contains(id)))
                {
                    var source = sources.Single(member => native.GetMappings().Any(map => map.TryGetValue(member.SolidId, out var id) && id == solid));
                    if (candidates.Values.Any(candidate => candidate.Source.LineId == source.LineId)) continue;
                    context.Rejected.Add(solid);
                    rejectedPhysicalOnly = true;
#if DEBUG
                    document.Editor.WriteMessage($"\nROOF_ORDINARY_{context.Operation}_LIFECYCLE sourceLine={source.LineId.Handle} " +
                        $"sourceSolid={source.SolidId.Handle} nativeSolidClone={solid.Handle} cloneLine=- cloneIndependentMemberId=- " +
                        "attachedManualWritten=false terminalHandled=true result=rejected-physical-only");
#endif
                }
                foreach (var source in replaced)
                {
                    if (!source.Independent)
                        RoofGeneratedRafterSetService.PreserveRecipe(document.Database, transaction, source.OwnerId,
                            context.Packages.Members.Where(m => !m.Independent && m.OwnerId == source.OwnerId).Select(m => m.PlanCopy));
                    if (!source.OwnerId.IsNull)
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction,
                            source.OwnerId, source.Entities.Select(e => e.Id).ToArray());
                    var retainedLines = candidates.Values.Where(c => c.EraseSource).Select(c => c.Line).ToHashSet();
                    foreach (var saved in source.Entities.Where(e => !retainedLines.Contains(e.Id)))
                        if (!saved.Id.IsErased && transaction.GetObject(saved.Id, OpenMode.ForWrite) is Entity entity) entity.Erase();
                }
                foreach (var candidate in candidates.Values)
                {
                    var member = CreateMember(document, transaction, candidate, context.Mirror);
                    copied.Add(member);
                    RoofOrdinaryGripLifecycleService.Accept(document, transaction, member, candidate.Plan);
                }
                var designations = RoofOrdinaryGripLifecycleService.RecalculateDesignations(document, transaction, copied);
                foreach (var source in sources)
                {
                    if (source.OwnerId.IsNull) continue;
                    if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, source.OwnerId))
                        throw new InvalidOperationException("Ordinary COPY canonical GROUP reconciliation failed.");
                }
                // Exact source snapshot comparison includes identity, ElementId, complete
                // annotation inventory, physical BRep and the original GROUP membership.
                RoofOrdinaryGripLifecycleService.Verify(document, transaction, context.Packages,
                    sources.Except(replaced).Select(source => (source, RoofOrdinaryGripChange.None, Axis(source.PlanCopy))).ToArray(),
                    false, removedGroupIds: removedSourceIds);
                RoofOrdinaryGripLifecycleService.Verify(document, transaction, context.Packages,
                    copied.Select(member => (member, RoofOrdinaryGripChange.Middle, Axis((Line)transaction.GetObject(member.LineId, OpenMode.ForRead)))).ToArray(),
                    true, designations, removedSourceIds);
                VerifyPackages(document, transaction, copied);
                VerifyReplacements(transaction, replaced, candidates.Values);
                VerifyCanonicalGroups(document, transaction, context.Packages, sources, removedSourceIds);
                messages = copied.Select(member => TraceMessage(transaction, candidates[member.LineId], member, context.Operation, mirrorDecision)).ToArray();
                transaction.Commit();
                foreach (var member in copied) context.Processed.Add(member.LineId);
            }
            catch (Exception ex)
            {
                messages = Array.Empty<string>();
                // Abort above restores every plugin write. Discard only mapped native clones;
                // never turn an unsuccessful Ordinary COPY into AttachedManual recovery.
                using var cleanup = document.Database.TransactionManager.StartTransaction();
                foreach (var source in sources)
                    if (!source.OwnerId.IsNull)
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, cleanup, source.OwnerId, nativeClones.ToArray());
                foreach (var id in nativeClones)
                    if (!id.IsErased && cleanup.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
                if (context.Mirror)
                {
                    foreach (var source in sources) RoofOrdinaryGripLifecycleService.Restore(cleanup, source);
                    foreach (var ownerId in sources.Select(s => s.OwnerId).Where(id => !id.IsNull).Distinct())
                    {
                        var saved = context.Packages.Owners[ownerId];
                        var owner = (Polyline)cleanup.GetObject(ownerId, OpenMode.ForWrite);
                        RoofDefinitionStore.Write(owner, cleanup, RoofDefinitionStore.Read(saved.Copy).Data!);
                        RoofOrdinaryGripLifecycleService.RestoreGroup(cleanup, context.Packages, ownerId);
                    }
                }
                RoofOrdinaryGripLifecycleService.Verify(document, cleanup, context.Packages,
                    sources.Select(source => (source, RoofOrdinaryGripChange.None, Axis(source.PlanCopy))).ToArray(), false);
                cleanup.Commit();
                document.Editor.WriteMessage($"\nROOF_ORDINARY_{context.Operation}_LIFECYCLE terminalHandled=true attachedManualWritten=false result=fail reason={ex.Message}");
            }
            finally
            {
                foreach (var member in copied) member.PlanCopy.Dispose();
                native.ConsumeMemberClones(nativeClones);
                context.Claimed.UnionWith(nativeClones);
                context.Handled.UnionWith(nativeClones);
                context.Handled.UnionWith(sources.SelectMany(source => source.Entities.Select(entity => entity.Id)));
            }
        }
        foreach (var message in messages)
        {
            if (message.Length == 0) continue;
            AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info($"ROOF_ORDINARY_{context.Operation}_LIFECYCLE", message);
            document.Editor.WriteMessage("\n" + message);
        }
        if (rejectedPhysicalOnly) RoofPhysical3DWarningService.Show();
        return context.Handled;
    }

    private static Member CreateMember(Document document, Transaction transaction, Candidate candidate, bool mirror)
    {
        var line = (Line)transaction.GetObject(candidate.Line, OpenMode.ForRead);
        var source = candidate.Source;
        RoofOrdinaryPhysicalBuildState? state;
        if (source.BuildState is null || !(mirror
                ? RoofOrdinaryMirrorRules.TryPreparePhysicalMirror(source.BuildState, candidate.Plan, out state)
                : RoofOrdinaryCopyCloneRules.TryPreparePhysicalCopy(source.BuildState, candidate.Plan, out state)))
            throw new InvalidOperationException("Ordinary COPY physical state is unavailable.");
        var identity = RoofIndependentOrdinaryTimberStore.Read(line)!;
        var solid = source.SolidId.IsNull ? new Solid3d { LayerId = line.LayerId } :
            (Solid3d)source.Entities.Single(entity => entity.Id == source.SolidId).Copy.Clone();
        var model = (BlockTableRecord)transaction.GetObject(line.OwnerId, OpenMode.ForWrite);
        var solidId = model.AppendEntity(solid);
        transaction.AddNewlyCreatedDBObject(solid, true);
        RoofIndependentOrdinaryTimberStore.TransferFromRoof(solid, transaction,
            identity with { EntityRole = RoofIndependentOrdinaryEntityRole.PhysicalSolid },
            RoofPhysical3DGeneratedStore.RegAppName, RoofGeneratedTimberStore.RegAppName, RoofGeneratedTimberStore.LinkRegAppName);
        var copy = (Line)line.Clone();
        return source with { Independent = true, LineId = candidate.Line, SolidId = solidId, PlanCopy = copy,
            BuildState = state, Entities = new[] { new RoofOrdinaryGripLifecycleService.EntitySnapshot(candidate.Line, copy) } };
    }

    private static void RollbackMirror(Document document, Transaction transaction, Context context,
        IEnumerable<Member> sources, IEnumerable<ObjectId> nativeClones)
    {
        var clones = nativeClones.ToArray();
        foreach (var source in sources)
            if (!source.OwnerId.IsNull)
                RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, source.OwnerId, clones);
        foreach (var id in clones)
            if (!id.IsErased && transaction.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
        foreach (var source in sources) RoofOrdinaryGripLifecycleService.Restore(transaction, source);
        foreach (var ownerId in sources.Select(s => s.OwnerId).Where(id => !id.IsNull).Distinct())
        {
            var saved = context.Packages.Owners[ownerId];
            var owner = (Polyline)transaction.GetObject(ownerId, OpenMode.ForWrite);
            RoofDefinitionStore.Write(owner, transaction, RoofDefinitionStore.Read(saved.Copy).Data!);
            RoofOrdinaryGripLifecycleService.RestoreGroup(transaction, context.Packages, ownerId);
        }
        RoofOrdinaryGripLifecycleService.Verify(document, transaction, context.Packages,
            sources.Select(source => (source, RoofOrdinaryGripChange.None, Axis(source.PlanCopy))).ToArray(), false);
        if (clones.Any(id => !id.IsErased)) throw new InvalidOperationException("Ordinary MIRROR rollback left native clones.");
    }

    private static string RejectedMirrorMessage(Candidate candidate, AcKrovy.Core.Services.MemberWarningDecision decision)
    {
#if DEBUG
        var source = candidate.Source;
        return $"ROOF_ORDINARY_MIRROR_LIFECYCLE sourceLine={source.LineId.Handle} sourceSolid={(source.SolidId.IsNull ? "-" : source.SolidId.Handle.ToString())} " +
            $"sourceState={(source.Independent ? "Independent" : "AUTO")} eraseSource={candidate.EraseSource} " +
            $"decision={(source.Independent ? "NONE" : "NO")} detached=False rollback=True physicalRebuild=False " +
            $"confirmationShown={decision.ConfirmationShown} automaticConfirm={decision.AutomaticConfirm} " +
            $"warningPreferenceChanged={decision.WarningPreferenceChanged} sourceElementId={source.ElementId} " +
            "cloneIndependentMemberId=- designationChanged=False manualOverrideWritten=false suppressionWritten=False attachedManualWritten=false " +
            "duplicateGeneratedIdentityCount=0 duplicatePhysicalKeyCount=0 groupCanonical=True terminalHandled=true result=pass";
#else
        return string.Empty;
#endif
    }

    internal static void CancelMirror(Document document, Context? context, RoofNativeCloneSnapshot native,
        IReadOnlyCollection<ObjectId> modifiedIds, string phase)
    {
        if (context is null || context.ProcessingStarted) return;
        context.ProcessingStarted = true;
        context.Observe(native);
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var restored = new List<Member>();
            foreach (var source in context.Packages.Members)
            {
                if (!source.OwnerId.IsNull && (source.OwnerId.IsErased ||
                    modifiedIds.Contains(source.OwnerId) ||
                    native.IsSourceChanged((Polyline)transaction.GetObject(source.OwnerId, OpenMode.ForRead)) ||
                    native.GetMappings().Any(map => map.ContainsKey(source.OwnerId)))) continue;
                var clones = native.GetMappings().SelectMany(map => Scope(source, map, native).Values)
                    .Where(id => !native.IsMemberCloneConsumed(id)).Distinct().ToArray();
                if (clones.Length == 0 && !source.Entities.Any(e => modifiedIds.Contains(e.Id) || e.Id.IsErased)) continue;
                if (!source.OwnerId.IsNull)
                    RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, source.OwnerId, clones);
                foreach (var id in clones)
                    if (!id.IsErased && transaction.GetObject(id, OpenMode.ForWrite) is Entity entity) entity.Erase();
                RoofOrdinaryGripLifecycleService.Restore(transaction, source);
                restored.Add(source);
                native.ConsumeMemberClones(clones);
            }
            foreach (var ownerId in restored.Select(s => s.OwnerId).Where(id => !id.IsNull).Distinct())
                RoofOrdinaryGripLifecycleService.RestoreGroup(transaction, context.Packages, ownerId);
            RoofOrdinaryGripLifecycleService.Verify(document, transaction, context.Packages,
                restored.Select(source => (source, RoofOrdinaryGripChange.None, Axis(source.PlanCopy))).ToArray(), false);
            transaction.Commit();
        }
        context.Trace(phase);
    }

    private static void VerifyPackages(Document document, Transaction transaction, IReadOnlyList<Member> copied)
    {
        var model = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(document.Database), OpenMode.ForRead);
        var entities = model.Cast<ObjectId>().Where(id => !id.IsErased)
            .Select(id => transaction.GetObject(id, OpenMode.ForRead)).OfType<Entity>().ToArray();
        var ids = copied.Select(member => RoofIndependentOrdinaryTimberStore.Read(
            (Entity)transaction.GetObject(member.LineId, OpenMode.ForRead))!.IndependentMemberId).ToArray();
        if (ids.Distinct().Count() != copied.Count) throw new InvalidOperationException("Duplicate Independent COPY identity.");
        foreach (var identity in ids)
        {
            var package = entities.Where(entity => RoofIndependentOrdinaryTimberStore.Read(entity)?.IndependentMemberId == identity).ToArray();
            if (package.OfType<Line>().Count() != 1 || package.OfType<Solid3d>().Count() != 1 ||
                package.Any(entity => RoofGeneratedTimberStore.Read(entity).Data is not null ||
                    RoofPhysical3DGeneratedStore.Read(entity).Data is not null || RoofAttachedManualTimberStore.Read(entity).Data is not null))
                throw new InvalidOperationException("Ordinary COPY package/authority invariant failed.");
        }
        var generated = entities.OfType<Line>().Select(line => RoofGeneratedTimberStore.Read(line).Data)
            .Where(data => data is { MemberKind: RoofGeneratedTimberKind.Rafter }).ToArray();
        if (generated.GroupBy(data => (data!.RoofOwnerReference, RoofGeneratedMemberKey.From(data))).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Ordinary COPY duplicate Generated identity.");
        var physical = entities.Select(entity => RoofPhysical3DGeneratedStore.Read(entity).Data).Where(data => data is not null).ToArray();
        if (physical.GroupBy(data => (data!.RoofOwnerReference, data.Role, data.StructuralId)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Ordinary COPY duplicate Physical3D key.");
    }

    private static void VerifyReplacements(Transaction transaction, IEnumerable<Member> sources, IEnumerable<Candidate> candidates)
    {
        var retained = candidates.Where(c => c.EraseSource).Select(c => c.Line).ToHashSet();
        foreach (var source in sources)
        {
            if (source.Entities.Any(e => !retained.Contains(e.Id) && !e.Id.IsErased))
                throw new InvalidOperationException("Ordinary MIRROR source package survives replacement.");
        }
    }

    private static void VerifyCanonicalGroups(Document document, Transaction transaction,
        RoofOrdinaryGripLifecycleService.Snapshot snapshot, IEnumerable<Member> sources, ISet<ObjectId> removed)
    {
        foreach (var ownerId in sources.Select(s => s.OwnerId).Where(id => !id.IsNull).Distinct())
        {
            if (!snapshot.Owners.TryGetValue(ownerId, out var owner) || owner.GroupId.IsNull) continue;
            var actual = ((Group)transaction.GetObject(owner.GroupId, OpenMode.ForRead)).GetAllEntityIds();
            var expected = owner.GroupMembers.Where(id => !removed.Contains(id)).ToHashSet();
            if (actual.Length != expected.Count || !actual.ToHashSet().SetEquals(expected))
                throw new InvalidOperationException("Ordinary clone exact canonical GROUP verification failed.");
#if DEBUG
            document.Editor.WriteMessage($"\nROOF_ORDINARY_CLONE_GROUP owner={ownerId.Handle} expected={expected.Count} " +
                $"actual={actual.Length} duplicates=0 missing=0 foreign=0 canonical=True stage=preCommit result=pass");
#endif
        }
    }

    private static string TraceMessage(Transaction transaction, Candidate candidate, Member copied, string operation,
        AcKrovy.Core.Services.MemberWarningDecision? decision)
    {
#if DEBUG
        var line = (Line)transaction.GetObject(copied.LineId, OpenMode.ForRead);
        _ = ElementDataStore.TryRead(line, transaction, out var timber);
        var source = candidate.Source;
        var old = RoofIndependentOrdinaryTimberStore.Read(source.PlanCopy);
        var identity = RoofIndependentOrdinaryTimberStore.Read(line)!;
        var message = $"ROOF_ORDINARY_{operation}_LIFECYCLE owner={source.OwnerId} sourceLine={source.LineId.Handle} cloneLine={line.Handle} " +
            $"sourceSolid={(source.SolidId.IsNull ? "-" : source.SolidId.Handle.ToString())} cloneSolid={copied.SolidId.Handle} " +
            $"nativeSolidClone={(candidate.NativeSolid.IsNull ? "-" : candidate.NativeSolid.Handle.ToString())} " +
            $"sourceState={(source.Independent ? "Independent" : "AUTO")} sourceIndependentMemberId={old?.IndependentMemberId ?? "-"} " +
            $"cloneIndependentMemberId={identity.IndependentMemberId} eraseSource={candidate.EraseSource} planDx={candidate.Plan.Start.X - source.PlanCopy.StartPoint.X:R} " +
            $"planDy={candidate.Plan.Start.Y - source.PlanCopy.StartPoint.Y:R} nativeDz={candidate.NativeDz:R} planZNormalized={candidate.PlanZNormalized} " +
            $"physicalMode=rebuilt sourceElementId={source.ElementId} cloneElementId={timber?.ElementId} designationChanged={source.ElementId != timber?.ElementId} " +
            (operation == "MIRROR" ? $"decision={(source.Independent ? "NONE" : "YES")} detached={!source.Independent} rollback=False " : string.Empty) +
            (decision is not null ? $"confirmationShown={decision.ConfirmationShown} automaticConfirm={decision.AutomaticConfirm} " +
                $"warningPreferenceChanged={decision.WarningPreferenceChanged} " : string.Empty) +
            "suppressionWritten=False attachedManualWritten=false duplicateGeneratedIdentityCount=0 duplicatePhysicalKeyCount=0 groupCanonical=True terminalHandled=true result=pass";
        return message;
#else
        return string.Empty;
#endif
    }

    private static RoofGeneratedMemberGeometry Geometry(Line line) => new(
        new(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z), new(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
    private static RoofSegment3D Axis(Line line) => new(Geometry(line).Start, Geometry(line).End);
    private static Point3d Map(RoofPoint3D point) => new(point.X, point.Y, point.Z);
}
