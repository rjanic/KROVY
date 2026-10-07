using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.AutoCAD.UI;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Command-scoped native MOVE evidence for an Ordinary member. Plan2D alone is
/// user-editable; the paired Solid retains its previously built physical frame.
/// </summary>
internal static class RoofOrdinaryLogicalMoveService
{
    private const double GeometryTolerance = 0.001d;

    internal sealed record MemberSnapshot(
        bool Independent, ObjectId OwnerId, ObjectId LineId, ObjectId SolidId,
        string MemberKey, string ElementId, Point3d LineStart, Point3d LineEnd,
        Point3d SolidCenter, double SolidVolume, IReadOnlyList<ObjectId> AnnotationIds,
        IReadOnlyList<ObjectId>? RoofGroupMemberIds);

    internal sealed record Snapshot(IReadOnlyList<MemberSnapshot> Members);

    private sealed record LineCandidate(ObjectId Id, Line Line, string Key, bool Independent,
        ObjectId OwnerId, string ElementId);

    private sealed record SolidCandidate(ObjectId Id, string Key, bool Independent,
        Point3d Center, double Volume);

    public static Snapshot Capture(Document document)
    {
        var members = new List<MemberSnapshot>();
        try
        {
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(document.Database), OpenMode.ForRead);
            var lines = new Dictionary<string, List<LineCandidate>>(StringComparer.OrdinalIgnoreCase);
            var solids = new Dictionary<string, List<SolidCandidate>>(StringComparer.OrdinalIgnoreCase);
            var annotationsBySource = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity ||
                    entity.IsErased) continue;
                if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity,
                        out var sourceHandle))
                    Add(annotationsBySource, sourceHandle, id);
                if (entity is Line line &&
                    metadataStore.TryRead(line, out var timber) && timber is not null &&
                    !string.IsNullOrWhiteSpace(timber.ElementId))
                {
                    var independent = RoofIndependentOrdinaryTimberStore.Read(line);
                    if (independent is { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } &&
                        RoofGeneratedTimberStore.Read(line).Data is null &&
                        RoofAttachedManualTimberStore.Read(line).Data is null)
                    {
                        Add(lines, "I:" + independent.IndependentMemberId,
                            new LineCandidate(id, line, independent.IndependentMemberId,
                                true, independent.SourceRoofReference is { } sourceRoof &&
                                    TryResolveOwner(document.Database, transaction, sourceRoof, out var sourceOwner)
                                        ? sourceOwner : ObjectId.Null, timber.ElementId));
                        continue;
                    }

                    if (RoofGeneratedTimberStore.Read(line).Data is
                            { MemberKind: RoofGeneratedTimberKind.Rafter } generated &&
                        independent is null &&
                        TryResolveOwner(document.Database, transaction,
                            generated.RoofOwnerReference, out var ownerId) &&
                        RoofOrdinaryRafterSolidMaterializationService.TryGetPlanPhysicalIdentity(
                            line, generated.RoofOwnerReference, out var physicalKey))
                        Add(lines, AutoKey(generated.RoofOwnerReference, physicalKey),
                            new LineCandidate(id, line, physicalKey, false, ownerId, timber.ElementId));
                }
                else if (entity is Solid3d solid)
                {
                    var independent = RoofIndependentOrdinaryTimberStore.Read(solid);
                    var physical = RoofPhysical3DGeneratedStore.Read(solid).Data;
                    string? key = independent is
                        { EntityRole: RoofIndependentOrdinaryEntityRole.PhysicalSolid } && physical is null
                        ? "I:" + independent.IndependentMemberId
                        : physical is { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } &&
                          independent is null
                            ? AutoKey(physical.RoofOwnerReference, physical.StructuralId)
                            : null;
                    if (key is null) continue;
                    try
                    {
                        var mass = solid.MassProperties;
                        Add(solids, key, new SolidCandidate(id, key,
                            independent is not null, mass.Centroid, mass.Volume));
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            }

            foreach (var pair in lines)
            {
                if (pair.Value.Count != 1 || !solids.TryGetValue(pair.Key, out var bodies) ||
                    bodies.Count != 1 ||
                    pair.Value[0].Independent != bodies[0].Independent) continue;
                var source = pair.Value[0];
                if (!source.Independent &&
                    !RoofUnsupportedStretchRecoverySnapshotService.TryGet(source.OwnerId, out _))
                    continue;
                var body = bodies[0];
                var annotations = annotationsBySource.TryGetValue(source.Line.Handle.ToString(),
                    out var found) ? found : new List<ObjectId>();
                IReadOnlyList<ObjectId>? groupMemberIds = null;
                if (!source.OwnerId.IsNull &&
                    RoofDisplayGroupService.TryOpenCanonicalGroup(document.Database, transaction,
                        source.OwnerId, OpenMode.ForRead, out var group) && group is not null)
                {
                    var relevant = new[] { source.Id, body.Id }.Concat(annotations).ToHashSet();
                    groupMemberIds = group.GetAllEntityIds().Where(relevant.Contains).ToArray();
                }
                members.Add(new MemberSnapshot(source.Independent, source.OwnerId, source.Id,
                    body.Id, source.Key, source.ElementId, source.Line.StartPoint,
                    source.Line.EndPoint, body.Center, body.Volume, annotations, groupMemberIds));
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // No writable state was captured; the pre-existing lifecycle remains in force.
            return new Snapshot(Array.Empty<MemberSnapshot>());
        }
        return new Snapshot(members);
    }

    public static IReadOnlyCollection<ObjectId> Process(Document document, Snapshot? snapshot,
        string? commandName, IReadOnlyCollection<ObjectId> nativeModifiedIds)
    {
        if (!RoofGeneratedMemberEditCommandRules.IsMoveCommand(commandName) || snapshot is null ||
            nativeModifiedIds.Count == 0) return Array.Empty<ObjectId>();
        var nativeSet = nativeModifiedIds.ToHashSet();
        var claimed = new HashSet<ObjectId>();
        var physicalRejected = false;
        var physicalRestoreFailed = false;
        foreach (var member in snapshot.Members)
        {
            if (RoofCommandLifecycleTerminalState.IsHandled(member.OwnerId))
            {
#if DEBUG
                document.Editor.WriteMessage(
                    $"\nROOF_ORDINARY_RECOVERY_SKIP owner={member.OwnerId.Handle} " +
                    $"command={commandName ?? "-"} reason=already-terminally-handled " +
                    "skip=ordinary-recovery,derived-solid-recovery,physical-reconcile");
#endif
                continue;
            }
            if ((!nativeSet.Contains(member.LineId) && !nativeSet.Contains(member.SolidId)) ||
                (!member.Independent && nativeSet.Contains(member.OwnerId))) continue;

            RoofOrdinaryLogicalMovePlan? plan;
            try
            {
                using var probe = document.Database.TransactionManager.StartTransaction();
                if (!TryObserve(document.Database, probe, member, out plan) ||
                    plan is null) continue;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }

            var editDecision = RoofOrdinaryAuthorityTransitionRules.Decide(
                member.Independent
                    ? RoofOrdinaryGeometryAuthority.Independent
                    : RoofOrdinaryGeometryAuthority.RoofOwned,
                commandName, geometryChanged: true, sourceChanged: false, roofLocked: false,
                editRepresentation: plan.Initiator == RoofOrdinaryMoveInitiator.Plan2D
                    ? RoofOrdinaryEditRepresentation.Plan2D
                    : RoofOrdinaryEditRepresentation.Physical3D);
            var directPhysical = editDecision == RoofOrdinaryEditDecision.RestoreDerivedPhysical;

            // The existing AUTO Plan2D confirmation/transfer remains authoritative.
            // When AutoCAD moved both representations, undo only the native Solid
            // displacement here; the Plan branch then applies its accepted delta.
            if (!member.Independent && plan.Initiator == RoofOrdinaryMoveInitiator.Plan2D)
            {
                if (!plan.SolidNativeMoved && Length(plan.LineTranslation) <= GeometryTolerance)
                    WriteAuthorityDiagnostic(document, member, plan,
                        "handoff-plan", "pending-plan-decision");
                if (plan.SolidNativeMoved || Length(plan.LineTranslation) > GeometryTolerance)
                {
                    try
                    {
                        using (document.LockDocument())
                        using (var transaction = document.Database.TransactionManager.StartTransaction())
                        {
                            if (plan.SolidNativeMoved)
                                RestorePhysicalToBaseline(transaction, member);
                            if (Length(plan.LineTranslation) > GeometryTolerance)
                                ApplyLineTranslation((Line)transaction.GetObject(
                                    member.LineId, OpenMode.ForWrite), plan.LineTranslation);
                            transaction.Commit();
                        }
                        if (plan.SolidNativeMoved) claimed.Add(member.SolidId);
                        WriteAuthorityDiagnostic(document, member, plan,
                            "reconcile-native-solid-before-plan", "handoff-plan");
                    }
                    catch (Exception ex)
                    {
                        var restored = TryRestoreAfterFailure(document, member, plan, nativeSet);
                        physicalRestoreFailed = !restored;
                        WriteAuthorityDiagnostic(document, member, plan,
                            "restore-derived", restored ? "restored-after-failure" : "failed",
                            ex.GetType().Name);
                        claimed.Add(member.LineId);
                        claimed.Add(member.SolidId);
                        claimed.UnionWith(member.AnnotationIds);
                    }
                }
                continue;
            }
            try
            {
                using (document.LockDocument())
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    if (!TryObserve(document.Database, transaction, member,
                            out var currentPlan) || currentPlan is null)
                        throw new InvalidOperationException("Ordinary MOVE evidence changed before transfer.");
                    if ((currentPlan.Initiator == RoofOrdinaryMoveInitiator.Physical3D) != directPhysical)
                        throw new InvalidOperationException("Ordinary MOVE representation changed before transfer.");
                    if (!directPhysical)
                        AcceptIndependentPlan(transaction, member, currentPlan, nativeSet);
                    else
                        RejectDirectPhysical(transaction, member, currentPlan, nativeSet);
                    transaction.Commit();
                }
                if (directPhysical)
                    physicalRejected = true;
                WriteAuthorityDiagnostic(document, member, plan,
                    directPhysical
                        ? "restore-derived" : "accept-plan",
                    directPhysical
                        ? "restored" : "accepted-from-plan");
                claimed.Add(member.LineId);
                claimed.Add(member.SolidId);
                claimed.UnionWith(member.AnnotationIds);
            }
            catch (Exception ex)
            {
                // The native MOVE happened before our transaction. Restore that part
                // from the command baseline in a fresh transaction after rollback.
                var restored = TryRestoreAfterFailure(document, member, plan, nativeSet);
                physicalRejected |= directPhysical;
                physicalRestoreFailed |= !restored;
                WriteAuthorityDiagnostic(document, member, plan,
                    "restore-derived", restored ? "restored-after-failure" : "failed",
                    ex.GetType().Name);
                claimed.Add(member.LineId);
                claimed.Add(member.SolidId);
                claimed.UnionWith(member.AnnotationIds);
            }
        }
        if (physicalRejected)
        {
            // The aggregate is command-scoped: several ObjectModified events or
            // several paired solids still produce one acknowledgement dialog.
            try
            {
                RoofPhysical3DWarningService.Show();
            }
            catch (Exception ex)
            {
                WriteUiWarningFailureDiagnostic(document, ex);
            }
            document.Editor.WriteMessage("\n" + AcKrovy.Localization.UiStrings.GetString(
                "Command_Roof_DerivedPhysicalMoveRejected"));
        }
        if (physicalRestoreFailed)
            document.Editor.WriteMessage("\n" + AcKrovy.Localization.UiStrings.GetString(
                "Command_Roof_DerivedPhysicalMoveRecoveryFailed"));
        return claimed;
    }

    private static void RejectDirectPhysical(Transaction transaction,
        MemberSnapshot member, RoofOrdinaryLogicalMovePlan plan, IReadOnlySet<ObjectId> nativeSet)
    {
        var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForRead);
        if (Length(plan.LineTranslation) > GeometryTolerance)
        {
            line.UpgradeOpen();
            ApplyLineTranslation(line, plan.LineTranslation);
        }
        var solid = (Solid3d)transaction.GetObject(member.SolidId, OpenMode.ForRead);
        if (member.Independent)
        {
            var lineIdentity = RoofIndependentOrdinaryTimberStore.Read(line);
            if (lineIdentity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } ||
                RoofIndependentOrdinaryTimberStore.Read(solid)?.IndependentMemberId !=
                lineIdentity.IndependentMemberId ||
                RoofGeneratedTimberStore.Read(line).Data is not null ||
                RoofPhysical3DGeneratedStore.Read(solid).Data is not null)
                throw new InvalidOperationException("Independent Ordinary identity changed during physical MOVE.");
        }
        else if (RoofGeneratedTimberStore.Read(line).Data is null ||
                 RoofPhysical3DGeneratedStore.Read(solid).Data is not
                     { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } physical ||
                 !string.Equals(physical.StructuralId, member.MemberKey, StringComparison.Ordinal))
            throw new InvalidOperationException("AUTO Ordinary identity changed during physical MOVE.");

        RestorePhysicalToBaseline(transaction, member);
        foreach (var id in member.AnnotationIds.Where(nativeSet.Contains))
        {
            if (id.IsErased) continue;
            ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).TransformBy(
                Matrix3d.Displacement(-ToVector(plan.NativeDelta)));
        }
        if (line.StartPoint.DistanceTo(member.LineStart) > GeometryTolerance ||
            line.EndPoint.DistanceTo(member.LineEnd) > GeometryTolerance ||
            !ElementIdMatches(transaction, line, member.ElementId) ||
            !GroupMembershipMatches(line.Database, transaction, member))
            throw new InvalidOperationException("Direct Physical3D MOVE modified authoritative Plan2D.");
    }

    private static void AcceptIndependentPlan(Transaction transaction,
        MemberSnapshot member, RoofOrdinaryLogicalMovePlan plan, IReadOnlySet<ObjectId> nativeSet)
    {
        var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForWrite);
        var solid = (Solid3d)transaction.GetObject(member.SolidId, OpenMode.ForWrite);
        var identity = RoofIndependentOrdinaryTimberStore.Read(line);
        if (identity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } ||
            RoofIndependentOrdinaryTimberStore.Read(solid)?.IndependentMemberId !=
            identity.IndependentMemberId ||
            RoofGeneratedTimberStore.Read(line).Data is not null ||
            RoofPhysical3DGeneratedStore.Read(solid).Data is not null)
            throw new InvalidOperationException("Independent Ordinary pairing changed during MOVE.");
        ApplyLineTranslation(line, plan.LineTranslation);
        if (Length(plan.SolidTranslation) > GeometryTolerance)
            solid.TransformBy(Matrix3d.Displacement(ToVector(plan.SolidTranslation)));
        foreach (var id in member.AnnotationIds)
        {
            if (id.IsErased) continue;
            var annotation = (Entity)transaction.GetObject(id, OpenMode.ForWrite);
            if (RoofIndependentOrdinaryTimberStore.Read(annotation)?.IndependentMemberId !=
                identity.IndependentMemberId)
                throw new InvalidOperationException("Independent Ordinary annotation pairing changed.");
            var correction = nativeSet.Contains(id)
                ? new RoofPoint3D(0d, 0d, plan.LineTranslation.Z)
                : plan.AnnotationTranslation;
            if (Length(correction) > GeometryTolerance)
                annotation.TransformBy(Matrix3d.Displacement(ToVector(correction)));
        }
        if (!ElementIdMatches(transaction, line, member.ElementId) ||
            line.StartPoint.DistanceTo(member.LineStart +
                new Vector3d(plan.NativeDelta.X, plan.NativeDelta.Y, 0d)) > GeometryTolerance ||
            line.EndPoint.DistanceTo(member.LineEnd +
                new Vector3d(plan.NativeDelta.X, plan.NativeDelta.Y, 0d)) > GeometryTolerance ||
            !SolidCenterMatches(solid, member.SolidCenter + ToVector(plan.NativeDelta)))
            throw new InvalidOperationException("Independent Ordinary MOVE synchronization failed.");
        var state = RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction);
        if (state is not null)
        {
            if (!RoofOrdinaryPhysicalBuildStateRules.TryRebase(state,
                    RoofIndependentOrdinaryPhysicalStateService.Axis(line), out state) || state is null)
                throw new InvalidOperationException("Independent Ordinary MOVE build-state rebase failed.");
            if (!RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, state, out state) || state is null)
                throw new InvalidOperationException("Independent Ordinary MOVE physical section unavailable.");
        }
        else
        {
            Polyline? owner = null;
            if (!member.OwnerId.IsNull && !member.OwnerId.IsErased)
                owner = transaction.GetObject(member.OwnerId, OpenMode.ForRead) as Polyline;
            if (!new AutoCadTimberElementMetadataStore(transaction).TryRead(line, out var timber) || timber is null ||
                !RoofIndependentOrdinaryPhysicalStateService.TryMigrate(line.Database, transaction, line, solid,
                    owner, timber, out state) || state is null)
                throw new InvalidOperationException("Independent Ordinary MOVE package migration failed.");
        }
        RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, state);
    }

    private static void RestorePhysicalToBaseline(Transaction transaction, MemberSnapshot member)
    {
        var solid = (Solid3d)transaction.GetObject(member.SolidId, OpenMode.ForWrite);
        var liveCenter = solid.MassProperties.Centroid;
        solid.TransformBy(Matrix3d.Displacement(member.SolidCenter - liveCenter));
        if (!SolidCenterMatches(solid, member.SolidCenter) ||
            Math.Abs(solid.MassProperties.Volume - member.SolidVolume) >
                Math.Max(GeometryTolerance, member.SolidVolume * 1e-8))
            throw new InvalidOperationException("Ordinary physical MOVE restoration failed.");
    }

    private static bool TryRestoreAfterFailure(Document document, MemberSnapshot member,
        RoofOrdinaryLogicalMovePlan plan, IReadOnlySet<ObjectId> nativeSet)
    {
        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var line = (Line)transaction.GetObject(member.LineId, OpenMode.ForWrite);
                line.StartPoint = member.LineStart;
                line.EndPoint = member.LineEnd;
                RestorePhysicalToBaseline(transaction, member);
                foreach (var id in member.AnnotationIds.Where(nativeSet.Contains))
                {
                    if (id.IsErased) continue;
                    ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).TransformBy(
                        Matrix3d.Displacement(-ToVector(plan.NativeDelta)));
                }
                if (!ElementIdMatches(transaction, line, member.ElementId) ||
                    !GroupMembershipMatches(document.Database, transaction, member)) return false;
                transaction.Commit();
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool TryObserve(Database database, Transaction transaction,
        MemberSnapshot member,
        out RoofOrdinaryLogicalMovePlan? plan)
    {
        plan = null;
        if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, member.LineId,
                OpenMode.ForRead, out var line, database) || line is null ||
            !AutoCadObjectIdAccess.TryGetObject<Solid3d>(transaction, member.SolidId,
                OpenMode.ForRead, out var solid, database) || solid is null)
            return false;
        var mass = solid.MassProperties;
        if (Math.Abs(mass.Volume - member.SolidVolume) >
            Math.Max(GeometryTolerance, member.SolidVolume * 1e-8)) return false;
        return RoofOrdinaryLogicalMoveRules.TryPlan(
            Point(member.LineStart), Point(member.LineEnd),
            Point(line.StartPoint), Point(line.EndPoint),
            Point(member.SolidCenter), Point(mass.Centroid), out plan);
    }

    private static bool ElementIdMatches(Transaction transaction, Line line, string elementId) =>
        new AutoCadTimberElementMetadataStore(transaction).TryRead(line, out var timber) &&
        string.Equals(timber?.ElementId, elementId, StringComparison.Ordinal);

    private static bool SolidCenterMatches(Solid3d solid, Point3d expected) =>
        solid.MassProperties.Centroid.DistanceTo(expected) <= GeometryTolerance;

    private static bool GroupMembershipMatches(Database database, Transaction transaction,
        MemberSnapshot member)
    {
        if (member.RoofGroupMemberIds is null) return true;
        if (!RoofDisplayGroupService.TryOpenCanonicalGroup(database, transaction,
                member.OwnerId, OpenMode.ForRead, out var group) || group is null) return false;
        var relevant = new[] { member.LineId, member.SolidId }
            .Concat(member.AnnotationIds).ToHashSet();
        return group.GetAllEntityIds().Where(relevant.Contains).ToHashSet()
            .SetEquals(member.RoofGroupMemberIds);
    }

    private static void ApplyLineTranslation(Line line, RoofPoint3D delta)
    {
        if (Length(delta) <= GeometryTolerance) return;
        var displacement = ToVector(delta);
        line.StartPoint += displacement;
        line.EndPoint += displacement;
    }

    private static bool TryResolveOwner(Database database, Transaction transaction,
        string handle, out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
            return false;
        try
        {
            ownerId = database.GetObjectId(false, new Handle(number), 0);
            return !ownerId.IsNull && transaction.GetObject(ownerId, OpenMode.ForRead) is Polyline owner &&
                   RoofDefinitionStore.Read(owner).Data is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var values)) map[key] = values = new List<T>();
        values.Add(value);
    }

    private static string AutoKey(string owner, string physical) => "A:" + owner + ":" + physical;
    private static RoofPoint3D Point(Point3d point) => new(point.X, point.Y, point.Z);
    private static Vector3d ToVector(RoofPoint3D point) => new(point.X, point.Y, point.Z);
    private static double Length(RoofPoint3D point) =>
        Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);

    private static void WriteAuthorityDiagnostic(Document document, MemberSnapshot member,
        RoofOrdinaryLogicalMovePlan plan, string action, string result, string? failure = null)
    {
#if DEBUG
        try
        {
            document.Editor.WriteMessage("\nROOF_ORDINARY_USER_GEOMETRY_AUTHORITY" +
                $" state={(member.Independent ? "INDEPENDENT" : "AUTO")}" +
                " command=MOVE authority=Plan2D" +
                $" initiator={plan.Initiator} lineHandle={member.LineId.Handle}" +
                $" solidHandle={member.SolidId.Handle}" +
                $" independentMemberId={(member.Independent ? member.MemberKey : "-")}" +
                $" nativeDx={plan.NativeDelta.X:R} nativeDy={plan.NativeDelta.Y:R}" +
                $" nativeDz={plan.NativeDelta.Z:R}" +
                $" planChanged={(plan.Initiator == RoofOrdinaryMoveInitiator.Plan2D ? "true" : "false")}" +
                $" solidChanged={(plan.SolidNativeMoved ? "true" : "false")}" +
                $" physicalWarning={(plan.Initiator == RoofOrdinaryMoveInitiator.Physical3D ? "true" : "false")}" +
                $" physicalWarningCount={(plan.Initiator == RoofOrdinaryMoveInitiator.Physical3D ? 1 : 0)}" +
                $" solidNativeChange={(plan.SolidNativeMoved ? "reconciled" : "none")}" +
                $" action={action} detach=false result={result} failure={failure ?? "-"}");
        }
        catch (Exception)
        {
            // Diagnostics must not turn a valid MOVE or its recovery into a failure.
        }
#endif
    }

    private static void WriteUiWarningFailureDiagnostic(Document document, Exception exception)
    {
#if DEBUG
        try
        {
            document.Editor.WriteMessage("\nROOF_ORDINARY_USER_GEOMETRY_AUTHORITY" +
                $" uiWarning=failed uiWarningException={exception.GetType().Name}");
        }
        catch (Exception)
        {
            // Diagnostics must never mask the completed physical restoration.
        }
#endif
    }
}
