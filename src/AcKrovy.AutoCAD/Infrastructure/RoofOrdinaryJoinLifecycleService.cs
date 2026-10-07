using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Member = AcKrovy.AutoCAD.Infrastructure.RoofOrdinaryGripLifecycleService.Member;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Command-scoped native graph reconciliation. One atomic many-to-one package.</summary>
internal static class RoofOrdinaryJoinLifecycleService
{
    internal sealed class Context : IDisposable
    {
        internal readonly RoofOrdinaryGripLifecycleService.Snapshot Packages;
        internal readonly Dictionary<ObjectId,Entity> Before = new();
        internal readonly Dictionary<ObjectId,ObjectId[]> Groups = new();
        internal readonly HashSet<ObjectId> Touched = new();
        internal readonly HashSet<ObjectId> Appended = new();
        internal readonly HashSet<ObjectId> Claimed = new();
        internal readonly string CommandId = Guid.NewGuid().ToString("N");
        private readonly Document _document;
        internal bool Terminal;
        internal int Processed, Results, Auto, Independent, Foreign, PendingRollback;
        internal int LogicalCandidates;
        internal bool Collinear, SectionCompatible, FrameCompatible;
        internal Context(Document document)
        {
            _document = document;
            Packages = RoofOrdinaryGripLifecycleService.Capture(document);
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(document.Database),OpenMode.ForRead);
            foreach (ObjectId id in model)
                if (!id.IsErased && transaction.GetObject(id,OpenMode.ForRead) is Curve curve)
                    Before.Add(id,(Entity)curve.Clone());
            // Native erase can remove GROUP membership before maintenance. This
            // read-only baseline also protects rejected non-Ordinary contributors.
            var groups = (DBDictionary)transaction.GetObject(document.Database.GroupDictionaryId,OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in groups)
                if (transaction.GetObject(entry.Value,OpenMode.ForRead) is Group group)
                    Groups.Add(entry.Value,group.GetAllEntityIds());
            Trace("begin");
        }
        internal void Observe(Entity entity,bool appended = false)
        {
            if (Terminal || entity is not Curve) return;
            Touched.Add(entity.ObjectId);
            if (appended) Appended.Add(entity.ObjectId);
        }
        internal void Trace(string phase)
        {
#if DEBUG
            Emit(_document,"ROOF_ORDINARY_JOIN_COMMAND_STATE",$"commandId={CommandId} phase={phase} " +
                $"rawCandidateCount={Touched.Count} logicalCandidateCount={LogicalCandidates} processedSourceCount={Processed} " +
                $"resultMemberCount={Results} autoCount={Auto} independentCount={Independent} nonKrovyCount={Foreign} " +
                $"collinear={Collinear} sectionCompatible={SectionCompatible} frameCompatible={FrameCompatible} " +
                $"pendingRollbackCount={PendingRollback} result={(phase == "disposed" ? "cleared" : "observed")}");
#endif
        }
        public void Dispose()
        {
            Packages.Dispose();
            foreach (var copy in Before.Values) copy.Dispose();
            Before.Clear(); Groups.Clear(); Touched.Clear(); Appended.Clear(); Claimed.Clear();
            Processed=Results=Auto=Independent=Foreign=PendingRollback=LogicalCandidates=0;
            Collinear=SectionCompatible=FrameCompatible=false; Terminal=true;
            Trace("disposed");
        }
    }

    internal static IReadOnlyCollection<ObjectId> Process(Document document,Context context,bool cancel = false,string? terminalPhase = null)
    {
        if (context.Terminal) return context.Claimed;
        context.Terminal=true;
        var raw = context.Before.Keys.Where(context.Touched.Contains).ToArray();
        var sources = context.Packages.Members.Where(m => raw.Contains(m.LineId)).ToArray();
        bool Changed(ObjectId id,Transaction transaction) => id.IsErased ||
            !SameCurve(context.Before[id],(Entity)transaction.GetObject(id,OpenMode.ForRead));
        bool nativeChanged;
        ObjectId[] results;
        using (var probe = document.Database.TransactionManager.StartTransaction())
        {
            nativeChanged = raw.Any(id => Changed(id,probe)) || context.Appended.Any(id => !id.IsErased);
            results = context.Touched.Where(id => !id.IsErased).ToArray();
        }
        // Notifications on unchanged geometry are native no-op evidence, not an edit.
        if (sources.Length == 0 && !raw.Any(id => IsOrdinary(context.Before[id]))) return context.Claimed;
        context.Claimed.UnionWith(raw); context.Claimed.UnionWith(context.Appended);
        context.Claimed.UnionWith(sources.SelectMany(m => m.Entities.Select(e => e.Id)));
        if (!nativeChanged) { context.Trace("noop"); return context.Claimed; }
        context.Auto=sources.Count(m => !m.Independent); context.Independent=sources.Length-context.Auto;
        context.LogicalCandidates=sources.Length;
        context.Foreign=raw.Length-sources.Length; context.PendingRollback=sources.Length;
        context.Trace("collected");
        var selection = RoofOrdinaryGripRollbackRefreshService.CaptureSelection(document.Editor);
        MemberWarningDecision? decision = null;
        RoofOrdinaryJoinOrder? order = null;
        Member? joined = null;
        RoofSegment3D? plan = null;
        string nativeType="-", failure="pass", identity="-";
        bool accepted=false, restored=false, normalized=false, groupCanonical=false;
        ObjectId finalLine=ObjectId.Null;
        try
        {
            if (cancel) throw new InvalidOperationException("NativeJoinCancelledOrFailed");
            if (context.Foreign != 0 || sources.Length < 2) throw new InvalidOperationException("UnsupportedOrUnresolvedJoinContributor");
            if (sources.Any(m => m.Signature.ElementType != AcKrovy.Core.Models.TimberElementType.Rafter))
                throw new InvalidOperationException("UnsupportedJoinTimberDefinition");
            if (results.Length != 1) throw new InvalidOperationException("AmbiguousNativeJoinResult");
            using (var probe = document.Database.TransactionManager.StartTransaction())
            {
                var result = (Entity)probe.GetObject(results[0],OpenMode.ForRead);
                nativeType=result.GetType().Name;
                plan = result is Line line && Math.Abs(line.StartPoint.Z) <= RoofFaceRafterLayoutService.CoordinateToleranceMm &&
                    Math.Abs(line.EndPoint.Z) <= RoofFaceRafterLayoutService.CoordinateToleranceMm
                    ? RoofOrdinaryGripLifecycleRules.Plan(Axis(line)) : result is Polyline polyline &&
                    RoofOrdinaryJoinRules.IsStraightPolyline(Enumerable.Range(0,polyline.NumberOfVertices).Select(i => Point(polyline.GetPoint3dAt(i))).ToArray(),
                        Enumerable.Range(0,polyline.NumberOfVertices).Select(polyline.GetBulgeAt).ToArray(),polyline.Closed,out var straight) ? straight : null;
                if (plan is null) throw new InvalidOperationException("BentOrUnsupportedNativeJoinResult");
                if (sources.Where(m => !m.Independent).Any(m => m.OwnerId.IsNull || m.OwnerId.IsErased ||
                        RoofDefinitionStore.Read((Polyline)probe.GetObject(m.OwnerId,OpenMode.ForRead)).Data?.EditState != RoofEditState.Unlocked))
                    throw new InvalidOperationException("AutomaticJoinRoofLockedOrUnavailable");
            }
            var inputs=sources.Select(m => new RoofOrdinaryJoinSource(Axis(m.PlanCopy),m.BuildState,m.Signature.Material)).ToArray();
            context.Collinear=RoofOrdinaryJoinRules.TryOrder(inputs.Select(s => s.Plan).ToArray(),plan,out order,out _);
            context.SectionCompatible=sources.All(m => m.Signature.Material==sources[0].Signature.Material &&
                Math.Abs(m.Signature.WidthMm-sources[0].Signature.WidthMm)<=RoofFaceRafterLayoutService.CoordinateToleranceMm &&
                Math.Abs(m.Signature.HeightMm-sources[0].Signature.HeightMm)<=RoofFaceRafterLayoutService.CoordinateToleranceMm);
            foreach (var source in sources)
            {
#if DEBUG
                Emit(document,"ROOF_ORDINARY_JOIN_SOURCE",$"line={source.LineId.Handle} state={(source.Independent ? "Independent" : "AUTO")} " +
                    $"independentMemberId={RoofIndependentOrdinaryTimberStore.Read(source.PlanCopy)?.IndependentMemberId ?? "-"} elementId={source.ElementId} " +
                    $"buildStateSource={(source.BuildState is null ? "unavailable" : source.BuildStateMigrated ? "migrated_member_package" : source.Independent ? "persistent_member_xrecord" : "live_roof_context")}");
#endif
                if (source.BuildState is null || (!source.SolidId.IsNull &&
                        !RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(source.BuildState,Axis(source.PlanCopy),
                            source.SolidVertices.Select(Point).ToArray())))
                    throw new InvalidOperationException("SourceCurrentPhysicalBodyReplayUnavailable:"+source.LineId.Handle);
            }
            if (!RoofOrdinaryJoinRules.TryPrepare(inputs,plan,out var state,out order,out var reason))
                throw new InvalidOperationException(reason);
            context.SectionCompatible=context.FrameCompatible=true;
            if (context.Auto > 0) decision=MemberWarningPreferenceService.ConfirmAutomaticDetach();
            accepted=decision?.Accepted ?? true;
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                if (!accepted)
                {
                    RestoreAll(document,transaction,context,raw,sources);
                    groupCanonical=true;
                }
                else
                {
                    var first=sources[order!.StartOwner];
                    foreach (var ownerId in sources.Where(m => !m.Independent).Select(m => m.OwnerId).Distinct())
                        RoofGeneratedRafterSetService.PreserveRecipe(document.Database,transaction,ownerId,
                            context.Packages.Members.Where(m => !m.Independent && m.OwnerId==ownerId).Select(m => m.PlanCopy));
                    var removed=sources.SelectMany(m => m.Entities.Select(e => e.Id)).ToHashSet();
                    foreach (var ownerId in sources.Select(m => m.OwnerId).Where(id => !id.IsNull).Distinct())
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database,transaction,ownerId,removed.ToArray());
                    var carrier=(Entity)transaction.GetObject(results[0],OpenMode.ForWrite);
                    if (carrier is Line)
                        finalLine=carrier.ObjectId;
                    else
                    {
                        var line=new Line(Map(plan.Start),Map(plan.End)) { LayerId=first.PlanCopy.LayerId,Visible=first.PlanCopy.Visible };
                        var model=(BlockTableRecord)transaction.GetObject(carrier.OwnerId,OpenMode.ForWrite);
                        finalLine=model.AppendEntity(line); transaction.AddNewlyCreatedDBObject(line,true);
                        carrier.Erase(); normalized=true;
                    }
                    var final=(Line)transaction.GetObject(finalLine,OpenMode.ForWrite);
                    if (!new AutoCadTimberElementMetadataStore(transaction).TryRead(first.PlanCopy,out var timber) || timber is null)
                        throw new InvalidOperationException("SourceTimberDefinitionUnavailable");
                    new AutoCadTimberElementMetadataStore(transaction).Write(final,timber);
                    identity=Guid.NewGuid().ToString("N");
                    var history=RoofIndependentOrdinaryTimberStore.Read(first.PlanCopy);
                    var generated=RoofGeneratedTimberStore.Read(first.PlanCopy).Data;
                    var ownership=new RoofIndependentOrdinaryTimberData(RoofIndependentOrdinaryTimberDataSchema.CurrentVersion,identity,
                        RoofIndependentOrdinaryOriginKind.Joined,RoofIndependentOrdinaryEntityRole.PlanLine,
                        history?.SourceRoofReference ?? generated?.RoofOwnerReference,history?.SourceGeneratedMemberKey ??
                            (generated is null ? null : RoofGeneratedMemberKey.From(generated)));
                    Transfer(final,transaction,ownership);
                    foreach (var source in sources)
                        foreach (var entity in source.Entities.Where(e => e.Id != finalLine))
                            if (!entity.Id.IsErased) ((Entity)transaction.GetObject(entity.Id,OpenMode.ForWrite)).Erase();
                    // A fresh solid avoids retaining any consumed physical identity.
                    var previousSolid=first.Entities.FirstOrDefault(e => e.Id==first.SolidId)?.Copy;
                    var solid=new Solid3d { LayerId=previousSolid?.LayerId ?? first.PlanCopy.LayerId,
                        Visible=previousSolid?.Visible ?? first.PlanCopy.Visible };
                    var space=(BlockTableRecord)transaction.GetObject(final.OwnerId,OpenMode.ForWrite);
                    var solidId=space.AppendEntity(solid); transaction.AddNewlyCreatedDBObject(solid,true);
                    Transfer(solid,transaction,ownership with { EntityRole=RoofIndependentOrdinaryEntityRole.PhysicalSolid });
                    final.StartPoint=Map(plan.Start); final.EndPoint=Map(plan.End);
                    var copy=(Line)final.Clone();
                    joined=first with { Independent=true,LineId=finalLine,SolidId=solidId,PlanCopy=copy,BuildState=state,
                        Entities=new[] { new RoofOrdinaryGripLifecycleService.EntitySnapshot(finalLine,copy) },
                        BuildStateMigrated=false,BuildStateTrace=null };
                    RoofOrdinaryGripLifecycleService.Accept(document,transaction,joined,plan);
                    var designations=RoofOrdinaryGripLifecycleService.RecalculateDesignations(document,transaction,new[] {joined});
                    foreach (var ownerId in sources.Select(m => m.OwnerId).Where(id => !id.IsNull).Distinct())
                        if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document,transaction,ownerId))
                            throw new InvalidOperationException("JoinCanonicalGroupReconciliationFailed");
                    RoofOrdinaryGripLifecycleService.Verify(document,transaction,context.Packages,
                        new[] { (joined,RoofOrdinaryGripChange.Both,plan) },true,designations,removed);
                    RoofOrdinaryRotateLifecycleService.VerifyPackages(document,transaction,new[] {joined},"JOIN");
                    VerifyConsumed(transaction,context,sources,joined,removed);
                    groupCanonical=true;
                }
                transaction.Commit(); restored=!accepted;
            }
        }
        catch (Exception exception)
        {
            accepted=false; failure="failed:"+exception.Message.Replace(' ','_');
            try
            {
                using (document.LockDocument())
                using (var rollback=document.Database.TransactionManager.StartTransaction())
                {
                    RestoreAll(document,rollback,context,raw,sources);
                    rollback.Commit(); restored=true; groupCanonical=true;
                }
            }
            catch (Exception restoreError) { failure+=";rollback_failed:"+restoreError.Message.Replace(' ','_'); }
        }
        finally { joined?.PlanCopy.Dispose(); }
        foreach (var ownerId in sources.Select(m => m.OwnerId).Where(id => !id.IsNull).Distinct())
        {
            context.Claimed.Add(ownerId);
            RoofCommandLifecycleTerminalState.MarkHandled(ownerId); RoofCommandLifecycleTerminalState.Owners.Add(ownerId);
        }
        context.Processed=sources.Length; context.Results=accepted ? 1 : 0; context.PendingRollback=restored || accepted ? 0 : sources.Length;
        if (restored) RoofOrdinaryGripRollbackRefreshService.Schedule(document,selection,
            sources.SelectMany(m => m.Entities.Select(e => e.Id)).Concat(raw).Distinct().ToArray(),sources.Select(m => m.LineId).ToArray());
        if (restored && failure != "pass" && !cancel)
            document.Editor.WriteMessage("\n"+AcKrovy.Localization.UiStrings.MessageOrdinaryJoinRejected);
#if DEBUG
        Emit(document,"ROOF_ORDINARY_JOIN_LIFECYCLE",$"sourceCount={sources.Length} sourceLines={string.Join(",",sources.Select(m => m.LineId.Handle))} " +
            $"sourceStates={string.Join(",",sources.Select(m => m.Independent ? "Independent" : "AUTO"))} autoCount={context.Auto} independentCount={context.Independent} " +
            $"finalLine={(accepted ? finalLine.Handle.ToString() : "-")} nativeResultType={nativeType} normalizedToLine={normalized} collinear={context.Collinear} " +
            $"gapCount={order?.GapCount ?? 0} overlapCount={order?.OverlapCount ?? 0} decision={(decision is null ? "NONE" : decision.Accepted ? "YES" : "NO")} " +
            $"confirmationShown={decision?.ConfirmationShown ?? false} automaticConfirm={decision?.AutomaticConfirm ?? false} newIndependentMemberId={(accepted ? identity : "-")} " +
            $"physicalRebuild={accepted} sourceCleanup={accepted} annotationsRebuilt={accepted} groupCanonical={groupCanonical} suppressionWritten=False " +
            $"manualOverrideWritten=False attachedManualWritten=False rollback={restored} terminalHandled=True result={failure}");
#endif
        context.Trace(cancel ? terminalPhase ?? "cancel" : "end");
        return context.Claimed;
    }

    private static void RestoreAll(Document document,Transaction transaction,Context context,ObjectId[] raw,Member[] sources)
    {
        foreach (var id in context.Appended.Where(id => !id.IsErased)) ((Entity)transaction.GetObject(id,OpenMode.ForWrite)).Erase();
        foreach (var id in raw)
        {
            var entity=(Entity)transaction.GetObject(id,OpenMode.ForWrite,true);
            if (entity.IsErased) entity.Erase(false);
            entity.CopyFrom(context.Before[id]); using var data=context.Before[id].XData; if (data is not null) entity.XData=data;
        }
        foreach (var source in sources) RoofOrdinaryGripLifecycleService.Restore(transaction,source);
        foreach (var saved in context.Groups.Where(g => g.Value.Any(raw.Contains)))
        {
            var group=(Group)transaction.GetObject(saved.Key,OpenMode.ForWrite);
            var actual=group.GetAllEntityIds();
            foreach (var id in actual.Except(saved.Value)) group.Remove(id);
            foreach (var id in saved.Value.Except(actual)) group.Append(id);
            if (!group.GetAllEntityIds().SequenceEqual(saved.Value))
            {
                // Native unerase may restore order differently. Reinsert the same
                // exact baseline once; this is rollback, never accepted deduplication.
                foreach (var id in group.GetAllEntityIds()) group.Remove(id);
                foreach (var id in saved.Value) group.Append(id);
            }
            if (!group.GetAllEntityIds().SequenceEqual(saved.Value)) throw new InvalidOperationException("JoinExactGroupRollbackFailed");
        }
        if (raw.Any(id => id.IsErased || !SameCurve(context.Before[id],(Entity)transaction.GetObject(id,OpenMode.ForRead))))
            throw new InvalidOperationException("JoinExactNativeGeometryRollbackFailed");
        RoofOrdinaryGripLifecycleService.Verify(document,transaction,context.Packages,
            sources.Select(m => (m,RoofOrdinaryGripChange.None,Axis(m.PlanCopy))).ToArray(),false);
    }
    private static void VerifyConsumed(Transaction transaction,Context context,Member[] sources,Member joined,ISet<ObjectId> removed)
    {
        if (sources.SelectMany(m => m.Entities).Any(e => e.Id != joined.LineId && !e.Id.IsErased))
            throw new InvalidOperationException("ConsumedJoinPackageSurvives");
        var final=(Line)transaction.GetObject(joined.LineId,OpenMode.ForRead);
        var id=RoofIndependentOrdinaryTimberStore.Read(final)!.IndependentMemberId;
        if (sources.Any(m => RoofIndependentOrdinaryTimberStore.Read(m.PlanCopy)?.IndependentMemberId==id))
            throw new InvalidOperationException("JoinRetainedSourceLogicalIdentity");
        foreach (var owner in context.Packages.Owners.Values.Where(o => sources.Any(s => s.OwnerId==o.Id) && !o.GroupId.IsNull))
        {
            var actual=((Group)transaction.GetObject(owner.GroupId,OpenMode.ForRead)).GetAllEntityIds();
            var expected=owner.GroupMembers.Where(id => !removed.Contains(id)).ToHashSet();
            if (actual.Length != expected.Count || !actual.ToHashSet().SetEquals(expected))
                throw new InvalidOperationException("JoinExactCanonicalGroupFailed");
        }
    }
    private static bool IsOrdinary(Entity e) => RoofIndependentOrdinaryTimberStore.Read(e) is { EntityRole:RoofIndependentOrdinaryEntityRole.PlanLine } ||
        RoofGeneratedTimberStore.Read(e).Data is { MemberKind:RoofGeneratedTimberKind.Rafter };
    private static bool SameCurve(Entity before,Entity after) => before is Line a && after is Line b
        ? RoofOrdinaryGripLifecycleRules.Classify(Axis(a),Axis(b))==RoofOrdinaryGripChange.None
        : before is Polyline p && after is Polyline q && RoofOrdinaryGripLifecycleService.SourceMatches(p,q);
    private static void Transfer(Entity entity,Transaction transaction,RoofIndependentOrdinaryTimberData identity) =>
        RoofIndependentOrdinaryTimberStore.TransferFromRoof(entity,transaction,identity,RoofGeneratedTimberStore.RegAppName,
            RoofGeneratedTimberStore.LinkRegAppName,RoofPhysical3DGeneratedStore.RegAppName,RoofAttachedManualTimberStore.RegAppName);
    private static RoofPoint3D Point(Point3d p) => new(p.X,p.Y,p.Z);
    private static RoofSegment3D Axis(Line line) => new(Point(line.StartPoint),Point(line.EndPoint));
    private static Point3d Map(RoofPoint3D p) => new(p.X,p.Y,p.Z);
    private static void Emit(Document document,string diagnostic,string fields)
    {
        document.Editor.WriteMessage("\n"+diagnostic+" "+fields);
        AcKrovy.AutoCAD.Diagnostics.AcKrovyDiagnostics.Info(diagnostic,diagnostic+" "+fields);
    }
}
