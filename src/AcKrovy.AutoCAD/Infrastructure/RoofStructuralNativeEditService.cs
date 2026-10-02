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
/// Ordinary automatic rafters are never candidates.
/// </summary>
internal static class RoofStructuralNativeEditService
{
    private sealed record Candidate(ObjectId Id, string Owner, string Key, bool Physical, bool Manual);

    public static IReadOnlySet<ObjectId> Process(Document document, string? commandName,
        IReadOnlyCollection<ObjectId> modifiedIds, IReadOnlyCollection<string> erasedHandles,
        IReadOnlyCollection<ObjectId> appendedIds, IReadOnlyCollection<ObjectId> appendedAnnotationIds)
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
                    if (mapped.StructuralData is { } structural &&
                        RoofStructuralEditRules.IsStructuralHipValleyRole(structural.LogicalKey.Role))
                        candidates.Add(new(id, mapped.OwnerHandle, structural.LogicalKey.ToString(), false, false));
                    else if (mapped.StructuralAttachedManualData is { } erasedManual)
                        candidates.Add(new(id, mapped.OwnerHandle,
                            RoofStructuralAttachedManualIdentityRules.PhysicalKey(erasedManual.ManualIdentity),
                            false, true));
                    else if (mapped.PhysicalData is { Role: RoofPhysical3DGeneratedRole.StructuralRafterSolid } erasedPhysical)
                        candidates.Add(new(id, mapped.OwnerHandle, erasedPhysical.StructuralId, true,
                            RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(erasedPhysical.StructuralId)));
                    continue;
                }
                if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(read, id, OpenMode.ForRead,
                        out var entity, document.Database) || entity is null) continue;
                // Ordinary Generated/AttachedManual rafters are intentionally ignored.
                if (entity is Line && RoofStructuralGeneratedStore.Read(entity).Data is { } plan &&
                    RoofStructuralEditRules.IsStructuralHipValleyRole(plan.StructuralRole))
                    candidates.Add(new(id, plan.RoofOwnerReference, plan.LogicalKey.ToString(), false, false));
                else if (entity is Line && RoofStructuralAttachedManualStore.Read(entity).Data is { } manual)
                    candidates.Add(new(id, manual.RoofOwnerReference,
                        RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity),
                        false, true));
                else if (entity is Solid3d && RoofPhysical3DGeneratedStore.Read(entity).Data is { } physical &&
                         physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid)
                    candidates.Add(new(id, physical.RoofOwnerReference, physical.StructuralId, true,
                        RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(physical.StructuralId)));
            }
        }
        foreach (var ownerCandidates in candidates.GroupBy(candidate => candidate.Owner, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveId(document.Database, ownerCandidates.Key, out var ownerId) ||
                !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var snapshot)) continue;
            var claimed = new List<Candidate>();
            var manualizedCloneIds = new HashSet<ObjectId>();
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
                    var mirrorCloneAnnotations = new Dictionary<ObjectId, TimberElementData>();
                    var metadata = new AutoCadTimberElementMetadataStore(transaction);
                    var snapshotHandles = snapshot.Assembly.TimberLines
                        .Select(timber => timber.EntityHandle)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var eraseCloneIds = new List<ObjectId>();
                    var stateChanged = false;
                    foreach (var candidate in ownerCandidates)
                    {
                        if (RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(
                                candidate.Id.Handle.ToString()))
                            continue;
                        var action = RoofStructuralEditRules.Classify(commandName, candidate.Physical, definition.EditState);
                        if (candidate.Physical)
                        {
                            if (action is RoofStructuralNativeAction.RebuildPhysical
                                or RoofStructuralNativeAction.RejectClone
                                or RoofStructuralNativeAction.AcceptManualClone)
                                claimed.Add(candidate);
                            continue;
                        }
                        if (action == RoofStructuralNativeAction.Unclaimed) continue;
                        var before = snapshot.Assembly.TimberLines.SingleOrDefault(timber =>
                            string.Equals(timber.EntityHandle, candidate.Id.Handle.ToString(), StringComparison.OrdinalIgnoreCase));
                        // Appended Generated/Manual MIRROR creates a new Manual identity;
                        // an existing Manual source edits the same identity (or stays unchanged).
                        // Existing Generated sources retain their restore policy.
                        if (action == RoofStructuralNativeAction.AcceptManualClone &&
                            RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName) &&
                            before is not null)
                            action = candidate.Manual
                                ? RoofStructuralNativeAction.AcceptPlan : RoofStructuralNativeAction.RejectClone;
                        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>(transaction, candidate.Id, OpenMode.ForWrite,
                                out var line, document.Database) || line is null)
                            throw new InvalidOperationException("Structural reference unavailable.");

                        if (candidate.Manual)
                        {
                            if (before is null)
                            {
                                // Appended clone of an existing Manual Structural / or
                                // Generated→Manual already converted in this command.
                                if (action == RoofStructuralNativeAction.AcceptManualClone && !line.IsErased)
                                {
                                    if (!TryEnsureManualCloneIdentity(
                                            line, transaction, ownerCandidates.Key, elevation,
                                            RoofStructuralEditRules.CreationKindForCommand(commandName), snapshot.Assembly,
                                            out var sourceHandle, out var manualFailure))
                                    {
                                        eraseCloneIds.Add(candidate.Id);
                                        throw new InvalidOperationException(
                                            "structural-manual-clone-" + manualFailure);
                                    }
                                    manualizedCloneIds.Add(candidate.Id);
                                    if (RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName))
                                    {
                                        // Consume only native appended annotations bound to this
                                        // source; recreate the clone's set from its final geometry.
                                        RoofMirrorCloneDetachService.DeleteStructuralMirrorCloneAnnotations(
                                            document, transaction, appendedAnnotationIds, sourceHandle);
                                        if (!metadata.TryRead(line, out var cloneTimber) || cloneTimber is null ||
                                            RoofStructuralAttachedManualStore.Read(line).Data is not { } cloneManual)
                                            throw new InvalidOperationException("Structural manual clone metadata unavailable.");
                                        mirrorCloneAnnotations[candidate.Id] = cloneTimber;
                                        claimed.Add(candidate with { Key = RoofStructuralAttachedManualIdentityRules.PhysicalKey(cloneManual.ManualIdentity) });
#if DEBUG
                                        document.Editor.WriteMessage($"\nROOF_STRUCT_MANUAL_MIRROR_CLONE source={sourceHandle} clone={line.Handle} sourceIdentity={candidate.Key} cloneIdentity={cloneManual.ManualIdentity} placementMode=RigidMirror annotations=canonical result=prepared\n");
#endif
                                    }
                                    else claimed.Add(candidate);
                                    continue;
                                }
                                if (action is not (RoofStructuralNativeAction.RejectClone
                                    or RoofStructuralNativeAction.RestorePlan))
                                    continue;
                                if (!line.IsErased) eraseCloneIds.Add(candidate.Id);
                                claimed.Add(candidate);
                                continue;
                            }

                            if (action == RoofStructuralNativeAction.AcceptManualClone)
                            {
                                // COPY source Manual Structural remains Manual — claim only.
                                claimed.Add(candidate);
                                if (!metadata.TryRead(line, out var sourceTimber) || sourceTimber is null)
                                    throw new InvalidOperationException("Structural manual metadata unavailable.");
                                annotations[candidate.Id] = sourceTimber;
                                continue;
                            }

                            if (action == RoofStructuralNativeAction.AcceptPlan)
                            {
                                if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(commandName) && line.IsErased)
                                {
                                    // Manual erase needs no Generated suppression tombstone.
                                    RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
                                        document.Database, transaction, ownerId, new[] { candidate.Id });
                                    TimberAnnotationService.DeleteForSourceHandle(
                                        document.Database, transaction, before.SourceHandle);
                                }
                                else if (!line.IsErased && RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName))
                                {
                                    line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
                                    line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
                                    // An unchanged source (MIRROR No) must not reflect its frame.
                                    if (before.Start.DistanceTo(Point(line.StartPoint)) > 1e-6 ||
                                        before.End.DistanceTo(Point(line.EndPoint)) > 1e-6)
                                    {
                                        if (RoofStructuralAttachedManualStore.Read(line).Data is not { Placement: { } frame } manualData ||
                                            !RoofStructuralManualPlacementRules.TryReflectFrame(
                                                frame, new(before.Start, before.End),
                                                new(Point(line.StartPoint), Point(line.EndPoint)), out var reflected) ||
                                            reflected is null)
                                            throw new InvalidOperationException("structural-manual-mirror-placement-invalid");
                                        var mirrored = RoofStructuralAttachedManualDataRules.WithPlacement(manualData, reflected);
                                        if (mirrored.Data is null)
                                            throw new InvalidOperationException("structural-manual-mirror-" + mirrored.Error);
                                        RoofStructuralAttachedManualStore.Write(line, transaction, mirrored.Data);
#if DEBUG
                                        document.Editor.WriteMessage($"\nROOF_STRUCT_MANUAL_MIRROR_PLACEMENT handle={line.Handle} manualId={manualData.ManualIdentity} placementMode=InPlaceRigidMirror result=updated\n");
#endif
                                    }
                                    else
                                    {
                                        // MIRROR No source: preserve its metadata and annotations.
                                        claimed.Add(candidate);
                                        continue;
                                    }
                                }
                                else if (!line.IsErased &&
                                         (RoofGeneratedMemberEditCommandRules.IsMoveCommand(commandName) ||
                                          RoofStructuralEditRules.IsPlanGeometryAcceptCommand(commandName)))
                                {
                                    var dx = line.StartPoint.X - before.Start.X;
                                    var dy = line.StartPoint.Y - before.Start.Y;
                                    line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
                                    line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
                                    if (RoofStructuralAttachedManualStore.Read(line).Data is { Placement: { } frame } manualData &&
                                        (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6))
                                    {
                                        var moved = RoofStructuralAttachedManualDataRules.WithPlacement(
                                            manualData, RoofStructuralManualPlacementRules.Translate(frame, dx, dy, 0));
                                        if (moved.Data is null)
                                            throw new InvalidOperationException("structural-manual-move-" + moved.Error);
                                        RoofStructuralAttachedManualStore.Write(line, transaction, moved.Data);
                                    }
                                }
                                else continue;
                            }
                            else
                            {
                                // Locked / RestorePlan / RejectClone: restore snapshot Plan.
                                if (line.IsErased) line.Erase(false);
                                line.StartPoint = new(before.Start.X, before.Start.Y, 0);
                                line.EndPoint = new(before.End.X, before.End.Y, 0);
                            }

                            if (!line.IsErased)
                            {
                                if (!metadata.TryRead(line, out var timber) || timber is null)
                                    throw new InvalidOperationException("Structural manual metadata unavailable.");
                                annotations[candidate.Id] = timber;
                            }
                            claimed.Add(candidate);
                            continue;
                        }

                        if (!expected.TryGetValue(candidate.Key, out var item) &&
                            !RoofStructuralEditRules.IsCloneRejectCommand(commandName) &&
                            !RoofStructuralEditRules.IsManualCloneAcceptCommand(commandName) &&
                            !RoofStructuralEditRules.IsPlanRestoreCommand(commandName)) continue;
                        if (before is null)
                        {
                            // Appended structural clone carrying inherited Generated LogicalKey.
                            if (action == RoofStructuralNativeAction.AcceptManualClone && !line.IsErased)
                            {
                                if (!TryConvertCloneToAttachedManual(
                                        line, transaction, ownerCandidates.Key, candidate.Key,
                                        elevation,
                                        RoofStructuralEditRules.CreationKindForCommand(commandName),
                                        out var convertFailure))
                                {
                                    // Fail closed: erase the ambiguous Generated duplicate, but
                                    // do not report COPY claim success — M1 requires Manual child.
                                    eraseCloneIds.Add(candidate.Id);
                                    claimed.Add(candidate);
                                    RoofAssemblyGroupSyncService.DetachMembersBeforeErase(
                                        document.Database, transaction, ownerId, eraseCloneIds);
                                    foreach (var id in eraseCloneIds)
                                    {
                                        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>(
                                                transaction, id, OpenMode.ForWrite, out var doomed,
                                                document.Database) ||
                                            doomed is null || doomed.IsErased) continue;
                                        TimberAnnotationService.DeleteForSourceHandle(
                                            document.Database, transaction, doomed.Handle.ToString());
                                        doomed.Erase(true);
                                    }
                                    eraseCloneIds.Clear();
                                    throw new InvalidOperationException(
                                        "structural-manual-convert-" + convertFailure);
                                }
                                if (RoofGeneratedMemberEditCommandRules.IsMirrorCommand(commandName))
                                {
                                    // Generated identity is already replaced. Use the same
                                    // canonical clone annotation batch as Manual→Manual MIRROR.
                                    var sourceIds = RoofStructuralGeneratedStore.FindByOwner(
                                            document.Database, transaction, ownerCandidates.Key)
                                        .Where(id => snapshotHandles.Contains(id.Handle.ToString()) &&
                                            AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForRead,
                                                out var sourceLine, document.Database) && sourceLine is not null &&
                                            RoofStructuralGeneratedStore.Read(sourceLine).Data?.LogicalKey.ToString() == candidate.Key)
                                        .ToArray();
                                    if (sourceIds.Length != 1)
                                        throw new InvalidOperationException("Structural Generated MIRROR annotation source ambiguous.");
                                    RoofMirrorCloneDetachService.DeleteStructuralMirrorCloneAnnotations(
                                        document, transaction, appendedAnnotationIds, sourceIds[0].Handle.ToString());
                                    if (!metadata.TryRead(line, out var cloneTimber) || cloneTimber is null ||
                                        RoofStructuralAttachedManualStore.Read(line).Data is not { } cloneManual)
                                        throw new InvalidOperationException("Structural converted clone annotation metadata unavailable.");
                                    mirrorCloneAnnotations[candidate.Id] = cloneTimber;
#if DEBUG
                                    document.Editor.WriteMessage($"\nROOF_STRUCT_GENERATED_MIRROR_CLONE source={sourceIds[0].Handle} clone={line.Handle} cloneIdentity={cloneManual.ManualIdentity} annotations=canonical result=prepared\n");
#endif
                                }
                                manualizedCloneIds.Add(candidate.Id);
                                claimed.Add(candidate);
                                continue;
                            }
                            if (action is not (RoofStructuralNativeAction.RejectClone or RoofStructuralNativeAction.RestorePlan
                                or RoofStructuralNativeAction.AcceptPlan))
                                continue; // Never claim an appended clone as its MOVE/ERASE source.
                            // AcceptPlan geometry (TRIM/BREAK fragments): erase inherited clones.
                            if (!line.IsErased) eraseCloneIds.Add(candidate.Id);
                            claimed.Add(candidate);
                            continue;
                        }
                        if (!expected.TryGetValue(candidate.Key, out item)) continue;
                        var expectedStart = new RoofPoint3D(item.Segment3D.Start.X, item.Segment3D.Start.Y, 0);
                        var expectedEnd = new RoofPoint3D(item.Segment3D.End.X, item.Segment3D.End.Y, 0);
                        if (before.Start.DistanceTo(expectedStart) > 1e-6 || before.End.DistanceTo(expectedEnd) > 1e-6)
                            throw new InvalidOperationException("Structural snapshot does not match authoritative placement.");
                        if (action == RoofStructuralNativeAction.AcceptManualClone)
                        {
                            // COPY source Generated Structural remains Generated — claim only.
                            claimed.Add(candidate);
                            if (!metadata.TryRead(line, out var sourceTimber) || sourceTimber is null)
                                throw new InvalidOperationException("Structural reference metadata unavailable.");
                            annotations[candidate.Id] = sourceTimber;
                            continue;
                        }
                        if (action == RoofStructuralNativeAction.AcceptPlan)
                        {
                            if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(commandName) && line.IsErased)
                            {
                                state = RoofStructuralEditRules.Upsert(state,
                                    RoofStructuralEditRules.Get(state, item.LogicalKey) with { Suppressed = true });
                                stateChanged = true;
                                RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, ownerId, new[] { candidate.Id });
                                TimberAnnotationService.DeleteForSourceHandle(document.Database, transaction, before.SourceHandle);
                            }
                            else if (!line.IsErased && RoofGeneratedMemberEditCommandRules.IsMoveCommand(commandName) &&
                                     RoofStructuralEditRules.TryAcceptMove(state, item.LogicalKey, new(before.Start, before.End),
                                         new(Point(line.StartPoint), Point(line.EndPoint)), out var acceptedMove))
                            {
                                state = acceptedMove;
                                stateChanged = true;
                                line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
                                line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
                            }
                            else if (!line.IsErased && RoofStructuralEditRules.IsPlanGeometryAcceptCommand(commandName) &&
                                     canonical.Items.FirstOrDefault(planItem =>
                                         planItem.LogicalKey == item.LogicalKey) is { } automaticItem &&
                                     RoofStructuralEditRules.TryAcceptPlanGeometry(state, item.LogicalKey,
                                         new(Point(line.StartPoint), Point(line.EndPoint)),
                                         automaticItem.Segment3D, out var acceptedGeometry))
                            {
                                state = acceptedGeometry;
                                stateChanged = true;
                                // Persist authoritative Plan2D with Z forced to 0.
                                line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
                                line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
                            }
                            else continue; // Non-planar, off-fold, or invalid geometry remains unclaimed.
                        }
                        else
                        {
                            // RestorePlan / RejectClone: keep the canonical Plan2D source.
                            if (line.IsErased) line.Erase(false);
                            line.StartPoint = new(before.Start.X, before.Start.Y, 0);
                            line.EndPoint = new(before.End.X, before.End.Y, 0);
                        }
                        if (!line.IsErased)
                        {
                            if (!metadata.TryRead(line, out var timber) || timber is null)
                                throw new InvalidOperationException("Structural reference metadata unavailable.");
                            annotations[candidate.Id] = timber;
                        }
                        claimed.Add(candidate);
                    }

                    // Erase any remaining unsnapshot Hip/Valley under this owner (ARRAY
                    // collateral, BREAK/TRIM fragments, or clones not present in the event set).
                    if (claimed.Count > 0 &&
                        (RoofStructuralEditRules.IsCloneRejectCommand(commandName) ||
                         RoofStructuralEditRules.IsManualCloneAcceptCommand(commandName) ||
                         RoofStructuralEditRules.IsPlanRestoreCommand(commandName) ||
                         RoofStructuralEditRules.IsPlanGeometryAcceptCommand(commandName)))
                    {
                        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                                     document.Database, transaction, ownerCandidates.Key))
                        {
                            if (snapshotHandles.Contains(id.Handle.ToString()) || eraseCloneIds.Contains(id)) continue;
                            if (RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(
                                    id.Handle.ToString())) continue;
                            if (!AutoCadObjectIdAccess.TryGetObject<Line>(transaction, id, OpenMode.ForWrite,
                                    out var orphan, document.Database) || orphan is null || orphan.IsErased) continue;
                            // Already converted to AttachedManual — leave alone.
                            if (RoofStructuralAttachedManualStore.Read(orphan).Data is not null) continue;
                            if (RoofStructuralGeneratedStore.Read(orphan).Data is not { } orphanData ||
                                !RoofStructuralEditRules.IsStructuralHipValleyRole(orphanData.StructuralRole)) continue;
                            eraseCloneIds.Add(id);
                            claimed.Add(new Candidate(id, orphanData.RoofOwnerReference, orphanData.LogicalKey.ToString(), false, false));
                        }
                    }

                    if (eraseCloneIds.Count > 0)
                    {
                        RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, ownerId, eraseCloneIds);
                        foreach (var id in eraseCloneIds)
                        {
                            if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>(transaction, id, OpenMode.ForWrite,
                                    out var clone, document.Database) || clone is null || clone.IsErased) continue;
                            TimberAnnotationService.DeleteForSourceHandle(document.Database, transaction, clone.Handle.ToString());
                            clone.Erase(true);
                        }
                    }

                    if (claimed.Count == 0) continue;
                    if (stateChanged && definition.EditState == RoofEditState.Unlocked)
                        RoofStructuralEditStateStore.Write(owner, transaction, state);
                    // Include native-erased ids: remove their old GROUP slots before rebuilding.
                    RoofAssemblyGroupSyncService.DetachMembersBeforeErase(document.Database, transaction, ownerId,
                        claimed.Where(candidate => candidate.Physical).Select(candidate => candidate.Id).ToArray());
                    // Existing Plan2D entities and item numbers remain intact. Running
                    // whole-set Plan materialization here would renumber untouched
                    // structural signature groups before mixed ordinary snapshot recovery.
                    TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                        document.Database, transaction, annotations, defaultProfile);
                    TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                        document.Database, transaction, mirrorCloneAnnotations, defaultProfile,
                        copySourcePreservation: true);
                    if (!RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(
                            document.Database, transaction, owner, geometry, resolution, document.Editor, out var physicalFailure))
                        throw new InvalidOperationException("structural-physical-" + physicalFailure);
                    // STRETCH/ROTATE/etc. can mutate structural display children in the
                    // crossing selection. GROUP sync requires a current display set;
                    // rebuild it after Plan2D restore + Physical3D reconcile, before sync.
                    var displayEdges = RoofPhysical3DLifecycleService.CreateOwnedDisplayEdges(owner, geometry);
                    var displaySignature = RoofWireframe.BuildGenerationSignature(displayEdges);
                    if (!RoofDisplayService.Rebuild(
                            document.Database, transaction, ownerId, owner.Handle.ToString(),
                            displayEdges, displaySignature, syncAssemblyGroup: false))
                        throw new InvalidOperationException("Structural display rebuild failed.");
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
                    !VerifyCommitted(document, ownerId, commandName, manualizedCloneIds))
                    throw new InvalidOperationException("Structural committed set/GROUP verification failed.");
#if DEBUG
                foreach (var candidate in claimed.Where(item => !item.Id.IsErased))
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

    private static bool TryEnsureManualCloneIdentity(
        Line line,
        Transaction transaction,
        string ownerReference,
        RoofPhysicalElevationData? elevation,
        RoofStructuralAttachedManualCreationKind creationKind,
        RoofUnsupportedStretchAssemblySnapshotData snapshot,
        out string sourceHandle, out string failureReason)
    {
        sourceHandle = string.Empty;
        failureReason = string.Empty;
        if (RoofStructuralAttachedManualStore.Read(line).Data is { } existing)
        {
            RoofStructuralAttachedManualDataValidationResult reminted;
            if (creationKind == RoofStructuralAttachedManualCreationKind.Mirror)
            {
                // Inherited UUID identifies the exact surviving pre-command source,
                // not another timber with the same Generated provenance key.
                RoofUnsupportedStretchTimberLineSnapshotData? source = null;
                foreach (var before in snapshot.TimberLines)
                {
                    if (!TryResolveId(line.Database, before.EntityHandle, out var sourceId) ||
                        !AutoCadObjectIdAccess.TryGetObject<Line>(transaction, sourceId, OpenMode.ForRead,
                            out var sourceLine, line.Database) || sourceLine is null ||
                        RoofStructuralAttachedManualStore.Read(sourceLine).Data is not { } sourceManual ||
                        sourceManual.ManualIdentity != existing.ManualIdentity ||
                        sourceManual.RoofOwnerReference != ownerReference)
                        continue;
                    if (source is not null || sourceManual != existing ||
                        before.Start.DistanceTo(Point(sourceLine.StartPoint)) > 1e-6 ||
                        before.End.DistanceTo(Point(sourceLine.EndPoint)) > 1e-6)
                    {
                        failureReason = "ManualMirrorSourceAmbiguousOrChanged";
                        return false;
                    }
                    source = before;
                }
                if (source is null)
                {
                    failureReason = "ManualMirrorSourceMissing";
                    return false;
                }
                sourceHandle = source.EntityHandle;
                line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
                line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
                reminted = RoofStructuralAttachedManualDataRules.CreateMirroredClone(
                    existing, new(source.Start, source.End), new(Point(line.StartPoint), Point(line.EndPoint)));
            }
            else
            {
                // Existing COPY/OFFSET allocation remains unchanged.
                reminted = RoofStructuralAttachedManualDataRules.Create(
                    ownerReference, RoofStructuralAttachedManualIdentityRules.Create(),
                    existing.SourceLogicalKey, creationKind, existing.WidthMm,
                    existing.HeightMode, existing.ExplicitHeightMm);
            }
            if (reminted.Data is null)
            {
                failureReason = reminted.Error.ToString();
                return false;
            }
            RoofStructuralAttachedManualStore.WriteReplacingGenerated(line, transaction, reminted.Data);
            line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
            line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
            return true;
        }

        if (RoofStructuralGeneratedStore.Read(line).Data is { } generated)
            return TryConvertCloneToAttachedManual(
                line, transaction, ownerReference, generated.LogicalKey.ToString(),
                elevation, creationKind, out failureReason);

        failureReason = "ManualCloneIdentityMissing";
        return false;
    }

    private static bool TryConvertCloneToAttachedManual(
        Line line,
        Transaction transaction,
        string ownerReference,
        string inheritedLogicalKey,
        RoofPhysicalElevationData? elevation,
        RoofStructuralAttachedManualCreationKind creationKind,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (RoofStructuralGeneratedStore.Read(line).Data is not { } generated ||
            !string.Equals(generated.LogicalKey.ToString(), inheritedLogicalKey, StringComparison.Ordinal))
        {
            failureReason = "CloneGeneratedIdentityMissing";
            return false;
        }

        var sourceKey = generated.LogicalKey;
        var width = elevation?.StructuralWidthMm ?? 120d;
        var heightMode = elevation?.StructuralHeightMode ?? RoofStructuralHeightMode.Automatic;
        double? explicitHeight = heightMode == RoofStructuralHeightMode.Explicit
            ? elevation?.StructuralExplicitHeightMm : null;
        var created = RoofStructuralAttachedManualDataRules.Create(
            ownerReference,
            RoofStructuralAttachedManualIdentityRules.Create(),
            sourceKey,
            creationKind,
            width,
            heightMode,
            explicitHeight);
        if (created.Data is null)
        {
            failureReason = created.Error.ToString();
            return false;
        }

        RoofStructuralAttachedManualStore.WriteReplacingGenerated(line, transaction, created.Data);
        // Same-transaction live reread of CURRENT entity XData — never snapshot-before.
        var generatedAfter = RoofStructuralGeneratedStore.Read(line);
        var manualAfter = RoofStructuralAttachedManualStore.Read(line);
        if (generatedAfter.Data is not null)
        {
            failureReason = "GeneratedIdentityNotCleared";
#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                "[ROOF_STRUCT_MANUALIZE_VERIFY] handle=" + line.Handle +
                " generatedAfter=present manualAfter=" +
                (manualAfter.Data is null ? "absent" : "present") +
                " fail=GeneratedIdentityNotCleared");
#endif
            return false;
        }
        if (manualAfter.Data is null)
        {
            failureReason = "ManualIdentityNotPersisted";
#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                "[ROOF_STRUCT_MANUALIZE_VERIFY] handle=" + line.Handle +
                " generatedAfter=absent manualAfter=absent fail=ManualIdentityNotPersisted");
#endif
            return false;
        }
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            "[ROOF_STRUCT_MANUALIZE_VERIFY] handle=" + line.Handle +
            " generatedAfter=absent manualAfter=present ok=1");
#endif
        line.StartPoint = new(line.StartPoint.X, line.StartPoint.Y, 0);
        line.EndPoint = new(line.EndPoint.X, line.EndPoint.Y, 0);
        return true;
    }

    private static bool VerifyCommitted(
        Document document,
        ObjectId ownerId,
        string? commandName,
        IReadOnlySet<ObjectId> manualizedCloneIds)
    {
        using var read = document.Database.TransactionManager.StartTransaction();
        var ownerReference = ownerId.Handle.ToString();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(document.Database, read, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(read, id, OpenMode.ForRead, out var line, document.Database) || line is null ||
                Math.Abs(line.StartPoint.Z) > 1e-6 || Math.Abs(line.EndPoint.Z) > 1e-6 ||
                RoofStructuralGeneratedStore.Read(line).Data is not { } data || !keys.Add(data.LogicalKey.ToString())) return false;
        }
        foreach (var id in RoofStructuralAttachedManualStore.FindByOwner(document.Database, read, ownerReference))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(read, id, OpenMode.ForRead, out var line, document.Database) || line is null ||
                Math.Abs(line.StartPoint.Z) > 1e-6 || Math.Abs(line.EndPoint.Z) > 1e-6 ||
                RoofStructuralAttachedManualStore.Read(line).Data is not { } manual ||
                !keys.Add(RoofStructuralAttachedManualIdentityRules.PhysicalKey(manual.ManualIdentity))) return false;
        }
        foreach (var id in manualizedCloneIds)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Line>(read, id, OpenMode.ForRead, out var manualLine, document.Database) ||
                manualLine is null || manualLine.IsErased ||
                RoofStructuralAttachedManualStore.Read(manualLine).Data is null ||
                RoofStructuralGeneratedStore.Read(manualLine).Data is not null)
                return false;
        }
        if (RoofStructuralEditRules.IsManualCloneAcceptCommand(commandName) &&
            manualizedCloneIds.Count == 0 &&
            RoofStructuralAttachedManualStore.FindByOwner(document.Database, read, ownerReference).Count == 0)
        {
            // COPY claimed with no surviving Manual child — not a successful M1 outcome.
            // Source-only claim without an appended clone is still valid (no-op COPY).
        }
        var physicalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(document.Database, read, ownerReference))
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
