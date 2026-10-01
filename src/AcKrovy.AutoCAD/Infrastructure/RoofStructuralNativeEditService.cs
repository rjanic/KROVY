using System.Globalization;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// First structural claim after whole-roof ownership, before generic member recovery.
/// Collectors are suppressed by the caller. All accepted state and derived entities
/// participate in the native command's existing undo scope, with no undo/redo writes.
/// </summary>
internal static class RoofStructuralNativeEditService
{
    private sealed record Candidate(ObjectId Id, string Owner, string Key, bool Physical);

    public static IReadOnlySet<ObjectId> Process(Document document, string? commandName,
        IReadOnlyCollection<ObjectId> modifiedIds, IReadOnlyCollection<string> erasedHandles,
        IReadOnlyCollection<ObjectId> appendedIds)
    {
        var handled = new HashSet<ObjectId>();
        if (LiveGeometryCommandRules.IsUndoRedoCommand(commandName) ||
            !RoofStructuralEditRules.HasFirstClaimOpportunity(commandName)) return handled;
        var candidates = new List<Candidate>();
        using var documentLock = document.LockDocument();
        using (var read = document.Database.TransactionManager.StartTransaction())
        {
            var ids = modifiedIds.Concat(appendedIds).ToHashSet();
            foreach (var handle in erasedHandles)
                if (TryResolveId(document.Database, handle, out var id)) ids.Add(id);
            foreach (var id in ids)
            {
                // Native ERASE can make XData unreadable. Use the existing true
                // pre-command map, retaining the exact structural semantic key.
                if (id.IsErased && RoofDisplayErasePreCommandMapService.TryResolve(id.Handle.ToString(), out var mapped))
                {
                    if (mapped.StructuralData is { } structural)
                        candidates.Add(new(id, mapped.OwnerHandle, structural.LogicalKey.ToString(), false));
                    else if (mapped.PhysicalData is { Role: RoofPhysical3DGeneratedRole.StructuralRafterSolid } erasedPhysical)
                        candidates.Add(new(id, mapped.OwnerHandle, erasedPhysical.StructuralId, true));
                    continue;
                }
                if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(read, id, OpenMode.ForRead,
                        out var entity, document.Database) || entity is null) continue;
                if (entity is Line && RoofStructuralGeneratedStore.Read(entity).Data is { } plan &&
                    plan.StructuralRole is RoofStructuralRole.Hip or RoofStructuralRole.Valley)
                    candidates.Add(new(id, plan.RoofOwnerReference, plan.LogicalKey.ToString(), false));
                else if (entity is Solid3d && RoofPhysical3DGeneratedStore.Read(entity).Data is { } physical &&
                         physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    candidates.Add(new(id, physical.RoofOwnerReference, physical.StructuralId, true));
            }
        }
        foreach (var ownerCandidates in candidates.GroupBy(candidate => candidate.Owner, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveId(document.Database, ownerCandidates.Key, out var ownerId) ||
                !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var snapshot)) continue;
            var claimed = new List<Candidate>();
            try
            {
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(transaction, ownerId, OpenMode.ForRead,
                            out var owner, document.Database) || owner is null ||
                        RoofDefinitionStore.Read(owner).Data is not { } definition) continue;
                    var input = RoofPolylineExtractor.Extract(owner);
                    var normal = owner.Normal;
                    // Source edits / full-roof transformations retain their existing priority.
                    if (!RoofUnsupportedStretchRecoveryRules.SourceGeometryMatchesSnapshot(input,
                            RoofPolylineExtractor.GetSourceElevation(owner), new(normal.X, normal.Y, normal.Z), snapshot.Data)) continue;
                    var footprint = RoofFootprintValidator.Validate(input).Footprint;
                    if (footprint is null || RoofDefinitionPersistence.Restore(input, footprint, definition).Geometry is not HipRoofGeometry geometry)
                        continue;
                    // A member edit consumes the existing owner boundary identity;
                    // it must not create or repair roof topology provenance.
                    var identity = RoofBoundaryIdentityStore.Read(owner).Data;
                    if (identity is null) continue;
                    var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry,
                        RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity));
                    var state = RoofStructuralEditStateStore.Read(owner, transaction);
                    var defaultProfile = TimberElementDefaultProfileStore.Load();
                    var elevation = RoofPhysicalElevationStore.Read(owner).Data;
                    var canonical = RoofAutomaticStructuralRafterPlanner.Create(resolution, defaultProfile,
                        elevation is null ? null : RoofPhysicalElevationRules.ToState(elevation, geometry.RiseMm));
                    if (!canonical.IsValid) throw new InvalidOperationException("Structural canonical plan unavailable.");
                    var expected = RoofStructuralEditRules.ApplyPlan(canonical.Items, state).ToDictionary(item => item.LogicalKey.ToString());
                    var annotations = new Dictionary<ObjectId, TimberElementData>();
                    var metadata = new AutoCadTimberElementMetadataStore(transaction);
                    foreach (var candidate in ownerCandidates)
                    {
                        if (!expected.TryGetValue(candidate.Key, out var item)) continue;
                        var action = RoofStructuralEditRules.Classify(commandName, candidate.Physical, definition.EditState);
                        if (candidate.Physical)
                        {
                            if (action == RoofStructuralNativeAction.RebuildPhysical) claimed.Add(candidate);
                            continue;
                        }
                        // COPY/MIRROR/BREAK/etc receive first opportunity, but do not yet
                        // gain foundation semantics. Legacy handles only these unclaimed edits.
                        if (action == RoofStructuralNativeAction.Unclaimed) continue;
                        var before = snapshot.Assembly.TimberLines.SingleOrDefault(timber =>
                            string.Equals(timber.EntityHandle, candidate.Id.Handle.ToString(), StringComparison.OrdinalIgnoreCase));
                        if (before is null) continue; // Never claim an appended clone as its source.
                        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>(transaction, candidate.Id, OpenMode.ForWrite,
                                out var line, document.Database) || line is null) throw new InvalidOperationException("Structural reference unavailable.");
                        var expectedStart = new RoofPoint3D(item.Segment3D.Start.X, item.Segment3D.Start.Y, 0);
                        var expectedEnd = new RoofPoint3D(item.Segment3D.End.X, item.Segment3D.End.Y, 0);
                        if (before.Start.DistanceTo(expectedStart) > 1e-6 || before.End.DistanceTo(expectedEnd) > 1e-6)
                            throw new InvalidOperationException("Structural snapshot does not match authoritative placement.");
                        if (action == RoofStructuralNativeAction.AcceptPlan)
                        {
                            if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(commandName) && line.IsErased)
                            {
                                state = RoofStructuralEditRules.Upsert(state,
                                    RoofStructuralEditRules.Get(state, item.LogicalKey) with { Suppressed = true });
                                RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, ownerId, new[] { candidate.Id });
                                TimberAnnotationService.DeleteForSourceHandle(document.Database, transaction, before.SourceHandle);
                            }
                            else if (!line.IsErased && RoofGeneratedMemberEditCommandRules.IsMoveCommand(commandName) &&
                                     RoofStructuralEditRules.TryAcceptMove(state, item.LogicalKey, new(before.Start, before.End),
                                         new(Point(line.StartPoint), Point(line.EndPoint)), out var accepted)) state = accepted;
                            else continue; // Non-planar or non-translation edit remains unclaimed.
                        }
                        else
                        {
                            if (line.IsErased) line.Erase(false);
                            line.StartPoint = new(before.Start.X, before.Start.Y, 0);
                            line.EndPoint = new(before.End.X, before.End.Y, 0);
                        }
                        if (!line.IsErased)
                        {
                            if (!metadata.TryRead(line, out var timber) || timber is null)
                                throw new InvalidOperationException("Structural reference metadata unavailable.");
                            annotations.Add(candidate.Id, timber);
                        }
                        claimed.Add(candidate);
                    }
                    if (claimed.Count == 0) continue;
                    if (claimed.Any(candidate => !candidate.Physical) && definition.EditState == RoofEditState.Unlocked)
                        RoofStructuralEditStateStore.Write(owner, transaction, state);
                    // Include native-erased ids: remove their old GROUP slots before rebuilding.
                    RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, ownerId,
                        claimed.Where(candidate => candidate.Physical).Select(candidate => candidate.Id).ToArray());
                    // Existing Plan2D entities and item numbers remain intact. Running
                    // whole-set Plan materialization here would renumber untouched
                    // structural signature groups before mixed ordinary snapshot recovery.
                    TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                        document.Database, transaction, annotations, defaultProfile);
                    if (!RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(
                            document.Database, transaction, owner, geometry, resolution, document.Editor, out var physicalFailure))
                        throw new InvalidOperationException("structural-physical-" + physicalFailure);
                    if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, ownerId))
                        throw new InvalidOperationException("Structural GROUP synchronization failed.");
                    transaction.Commit();
                }
                // Only committed claims may protect native events from legacy recovery.
                var planHandles = claimed.Where(candidate => !candidate.Physical).Select(candidate => candidate.Id.Handle.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                snapshot.ClaimStructural(planHandles);
                handled.UnionWith(claimed.Select(candidate => candidate.Id));
                foreach (var annotation in snapshot.Assembly.Annotations.Where(annotation => planHandles.Contains(annotation.SourceHandle)))
                    if (TryResolveId(document.Database, annotation.EntityHandle, out var id)) handled.Add(id);
                if (!RoofAssemblyGroupSyncService.TryFinalizeStructuralMembers(document, ownerId, commandName) ||
                    !VerifyCommitted(document, ownerId)) throw new InvalidOperationException("Structural committed set/GROUP verification failed.");
#if DEBUG
                foreach (var candidate in claimed)
                    document.Editor.WriteMessage($"\nROOF_STRUCTURAL_NATIVE_CLAIM owner={candidate.Owner} identity={candidate.Key} command={commandName} representation={(candidate.Physical ? "Physical3D" : "Plan2D")} action={(candidate.Physical ? "rebuilt" : "semantic-reconciled")} result=committed\n");
#endif
            }
            catch (System.Exception ex)
            {
                _ = ex;
#if DEBUG
                document.Editor.WriteMessage($"\nROOF_STRUCTURAL_NATIVE_CLAIM owner={ownerCandidates.Key} command={commandName} result=failed detail={ex.GetType().Name}:{ex.Message}\n");
#endif
                document.Editor.WriteMessage("\n" + UiStrings.GetString("Command_Roof_DerivedPhysicalMoveRecoveryFailed") + "\n");
            }
        }
        return handled;
    }

    private static bool VerifyCommitted(Document document, ObjectId ownerId)
    {
        using var read = document.Database.TransactionManager.StartTransaction();
        var plans = RoofStructuralGeneratedStore.FindByOwner(document.Database, read, ownerId.Handle.ToString());
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in plans)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(read, id, OpenMode.ForRead, out var line, document.Database) || line is null ||
                Math.Abs(line.StartPoint.Z) > 1e-6 || Math.Abs(line.EndPoint.Z) > 1e-6 ||
                RoofStructuralGeneratedStore.Read(line).Data is not { } data || !keys.Add(data.LogicalKey.ToString())) return false;
        }
        var physicalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(document.Database, read, ownerId.Handle.ToString()))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(read, id, OpenMode.ForRead, out var entity, document.Database) || entity is null) continue;
            var data = RoofPhysical3DGeneratedStore.Read(entity).Data;
            if (data?.Role != RoofPhysical3DGeneratedRole.StructuralRafterSolid) continue;
            if (entity is not Solid3d || !physicalKeys.Add(data.StructuralId)) return false;
        }
        var owner = (Polyline)read.GetObject(ownerId, OpenMode.ForRead);
        return RoofPhysicalElevationStore.Read(owner).Data?.Physical3DEnabled == true ? physicalKeys.SetEquals(keys) : physicalKeys.Count == 0;
    }

    private static RoofPoint3D Point(Point3d point) => new(point.X, point.Y, point.Z);
    private static bool TryResolveId(Database database, string handle, out ObjectId id)
    {
        id = ObjectId.Null;
        if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        try { id = database.GetObjectId(false, new Handle(value), 0); return !id.IsNull && id.IsValid; }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
    }
}
