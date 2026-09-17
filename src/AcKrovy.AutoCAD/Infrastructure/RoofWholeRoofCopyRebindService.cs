using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.AutoCAD.Settings;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Deterministic whole-roof same-DWG COPY / MIRROR lifecycle.
/// Detection is payload/event-based (never spatial): a RoofDefinition-bearing source
/// Polyline that appeared in THIS COPY or MIRROR (Erase source = No) command, paired
/// with its pre-command owner by decoded RoofDefinition payload equality, whose
/// complete CURRENT physical owned assembly (ordinary generated + structural generated
/// + AttachedManual) was appended with the inherited old-owner metadata. The branch
/// erases the transient generated clones, regenerates the canonical sets under the new
/// owner through the shared materializers, rebinds AttachedManual clones by their
/// logical anchor key, rebuilds display/annotations and syncs the canonical group.
/// A detected whole-roof set is registered as consumed BEFORE any work so the
/// per-member COPY/MIRROR services can never detach it — including when the rebind
/// itself fails (transaction rolls back, old owner stays untouched, no old-owner
/// AttachedManual conversion). Member-only MIRROR keeps the existing AttachedManual
/// promotion policy when completeness is not matched.
/// </summary>
internal static class RoofWholeRoofCopyRebindService
{
    private sealed record OwnerCandidate(
        string Handle,
        ObjectId PolylineId,
        IRoofGeometry Geometry,
        RoofDefinitionData? Definition);

    private sealed record AppendedGeneratedClone(string Handle, ObjectId Id, string OwnerReference);

    private sealed record AppendedStructuralClone(
        string Handle,
        ObjectId Id,
        RoofStructuralGeneratedData Data);

    private sealed record AppendedAttachedClone(string Handle, ObjectId Id, RoofAttachedManualTimberData Data);

    private sealed record WholeRoofPair(
        string OldOwner,
        string NewOwner,
        OwnerCandidate NewOwnerCandidate,
        IReadOnlyList<AppendedGeneratedClone> GeneratedClones,
        IReadOnlyList<AppendedStructuralClone> StructuralClones,
        IReadOnlyList<AppendedAttachedClone> AttachedManualClones);

    public static void Process(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> appendedTimberIds,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds)
    {
        var isCopy = LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName);
        var isMirror = RoofGeneratedMemberEditCommandRules.IsMirrorCommand(globalCommandName);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !(isCopy || isMirror) ||
            appendedTimberIds is null ||
            appendedTimberIds.Count == 0)
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var owners = CollectOwners(document.Database, transaction);
                if (owners.Count == 0)
                {
                    return;
                }

                var preOwnerHandles = new HashSet<string>(
                    RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandOwnerHandles(),
                    StringComparer.OrdinalIgnoreCase);
                var newOwners = owners
                    .Where(owner => !preOwnerHandles.Contains(owner.Handle))
                    .ToArray();
                if (newOwners.Length == 0)
                {
#if DEBUG
                    if (isMirror)
                    {
                        RoofGeneratedCopyLifecycleDiag.WriteWholeMirrorStage(
                            document.Editor,
                            "owner-map",
                            "-",
                            "-",
                            0,
                            appendedTimberIds.Count,
                            0,
                            0,
                            0,
                            0,
                            "miss",
                            preOwnerHandles.Count == 0
                                ? "pre-command-snapshot-empty"
                                : "no-new-owner-polyline");
                    }
#endif
                    return;
                }

                var appendedGenerated = CollectAppendedGeneratedClones(
                    document.Database,
                    transaction,
                    appendedTimberIds);
                var appendedStructural = CollectAppendedStructuralClones(
                    document.Database,
                    transaction,
                    appendedTimberIds);
                var appendedAttached = CollectAppendedAttachedClones(
                    document.Database,
                    transaction,
                    appendedTimberIds);

                var pairs = new List<WholeRoofPair>();
                var consumedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Full-set candidates: pre-existing owners whose complete CURRENT
                // physical assembly (suppressed members physically absent in BOTH the
                // pre-command snapshot and the appended set) was appended with the
                // inherited old-owner reference.
                var fullSetOldOwners = owners
                    .Where(owner => preOwnerHandles.Contains(owner.Handle))
                    .Where(owner =>
                    {
                        var preGenerated = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandGeneratedHandlesByOwner(owner.Handle);
                        var preStructural = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandStructuralGeneratedHandlesByOwner(owner.Handle);
                        var preAttached = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandAttachedManualHandlesByOwner(owner.Handle);
                        return RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(
                            preGenerated.Count,
                            preStructural.Count,
                            preAttached.Count,
                            appendedGenerated.Count(clone => string.Equals(
                                clone.OwnerReference,
                                owner.Handle,
                                StringComparison.OrdinalIgnoreCase)),
                            appendedStructural.Count(clone => string.Equals(
                                clone.Data.RoofOwnerReference,
                                owner.Handle,
                                StringComparison.OrdinalIgnoreCase)),
                            appendedAttached.Count(clone => string.Equals(
                                clone.Data.RoofOwnerReference,
                                owner.Handle,
                                StringComparison.OrdinalIgnoreCase)));
                    })
                    .ToArray();

#if DEBUG
                if (isMirror && fullSetOldOwners.Length == 0)
                {
                    foreach (var oldOwner in owners.Where(owner => preOwnerHandles.Contains(owner.Handle)))
                    {
                        var preGenerated = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandGeneratedHandlesByOwner(oldOwner.Handle);
                        var preStructural = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandStructuralGeneratedHandlesByOwner(oldOwner.Handle);
                        var preAttached = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandAttachedManualHandlesByOwner(oldOwner.Handle);
                        var generatedClones = appendedGenerated.Count(clone => string.Equals(
                            clone.OwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase));
                        var structuralClones = appendedStructural.Count(clone => string.Equals(
                            clone.Data.RoofOwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase));
                        var attachedClones = appendedAttached.Count(clone => string.Equals(
                            clone.Data.RoofOwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase));
                        if (generatedClones + structuralClones + attachedClones == 0)
                        {
                            continue;
                        }

                        RoofGeneratedCopyLifecycleDiag.WriteWholeMirrorStage(
                            document.Editor,
                            "candidate",
                            oldOwner.Handle,
                            newOwners.Length == 1 ? newOwners[0].Handle : "-",
                            preGenerated.Count,
                            generatedClones,
                            preStructural.Count,
                            structuralClones,
                            preAttached.Count,
                            attachedClones,
                            "miss",
                            "complete-assembly-predicate-false");
                    }
                }
#endif

                foreach (var oldOwner in fullSetOldOwners)
                {
                    var newCandidates = newOwners
                        .Where(candidate => RoofWholeRoofCopyIdentityRules.DefinitionsEquivalent(
                            candidate.Definition,
                            oldOwner.Definition))
                        .ToArray();
                    var pairing = RoofWholeRoofCopyIdentityRules.ClassifyPairing(newCandidates.Length);
                    if (pairing == RoofWholeRoofCopyIdentityRules.RoofWholeRoofCopyPairing.None)
                    {
                        // Complete timber set copied but its source Polyline was not
                        // (or its definition is unreadable / rewritten before pairing):
                        // not a whole-roof copy — the ordinary per-rafter path keeps its
                        // existing semantics.
#if DEBUG
                        if (isMirror)
                        {
                            var preGenerated = RoofGeneratedCopyPreCommandSnapshotService
                                .GetPreCommandGeneratedHandlesByOwner(oldOwner.Handle);
                            var preStructural = RoofGeneratedCopyPreCommandSnapshotService
                                .GetPreCommandStructuralGeneratedHandlesByOwner(oldOwner.Handle);
                            var preAttached = RoofGeneratedCopyPreCommandSnapshotService
                                .GetPreCommandAttachedManualHandlesByOwner(oldOwner.Handle);
                            RoofGeneratedCopyLifecycleDiag.WriteWholeMirrorStage(
                                document.Editor,
                                "predicate",
                                oldOwner.Handle,
                                newOwners.Length == 1 ? newOwners[0].Handle : "-",
                                preGenerated.Count,
                                appendedGenerated.Count(clone => string.Equals(
                                    clone.OwnerReference,
                                    oldOwner.Handle,
                                    StringComparison.OrdinalIgnoreCase)),
                                preStructural.Count,
                                appendedStructural.Count(clone => string.Equals(
                                    clone.Data.RoofOwnerReference,
                                    oldOwner.Handle,
                                    StringComparison.OrdinalIgnoreCase)),
                                preAttached.Count,
                                appendedAttached.Count(clone => string.Equals(
                                    clone.Data.RoofOwnerReference,
                                    oldOwner.Handle,
                                    StringComparison.OrdinalIgnoreCase)),
                                "miss",
                                "definition-equivalent-false");
                        }
#endif
                        continue;
                    }

                    if (pairing == RoofWholeRoofCopyIdentityRules.RoofWholeRoofCopyPairing.Ambiguous)
                    {
                        // Two new owners match this old owner's definition: pairing is
                        // not deterministic. Fail closed: consume the clones so they can
                        // never be detached under the old owner, but do not rebind.
                        var ambiguousGenerated = appendedGenerated
                            .Where(clone => string.Equals(
                                clone.OwnerReference,
                                oldOwner.Handle,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        var ambiguousAttached = appendedAttached
                            .Where(clone => string.Equals(
                                clone.Data.RoofOwnerReference,
                                oldOwner.Handle,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        var ambiguousStructural = appendedStructural
                            .Where(clone => string.Equals(
                                clone.Data.RoofOwnerReference,
                                oldOwner.Handle,
                                StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        foreach (var clone in ambiguousGenerated)
                        {
                            consumedHandles.Add(clone.Handle);
                        }

                        foreach (var clone in ambiguousStructural)
                        {
                            consumedHandles.Add(clone.Handle);
                        }

                        foreach (var clone in ambiguousAttached)
                        {
                            consumedHandles.Add(clone.Handle);
                        }

#if DEBUG
                        RoofGeneratedCopyLifecycleDiag.WriteWholeCopyDetect(
                            document.Editor,
                            oldOwner.Handle,
                            "-",
                            ambiguousGenerated.Length,
                            ambiguousStructural.Length,
                            ambiguousAttached.Length,
                            "ambiguous",
                            isMirror);
#endif
                        continue;
                    }

                    var newOwner = newCandidates[0];
                    if (pairs.Any(pair => string.Equals(
                            pair.NewOwner,
                            newOwner.Handle,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        // Two distinct full-set old owners resolve to the same new
                        // Polyline (identical twin definitions): fail closed for the
                        // new owner; the first pair keeps the rebind.
#if DEBUG
                        RoofGeneratedCopyLifecycleDiag.WriteWholeCopyDetect(
                            document.Editor,
                            oldOwner.Handle,
                            newOwner.Handle,
                            0,
                            0,
                            0,
                            "ambiguous",
                            isMirror);
#endif
                        continue;
                    }

                    var generatedClones = appendedGenerated
                        .Where(clone => string.Equals(
                            clone.OwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var attachedClones = appendedAttached
                        .Where(clone => string.Equals(
                            clone.Data.RoofOwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var structuralClones = appendedStructural
                        .Where(clone => string.Equals(
                            clone.Data.RoofOwnerReference,
                            oldOwner.Handle,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    pairs.Add(new WholeRoofPair(
                        oldOwner.Handle,
                        newOwner.Handle,
                        newOwner,
                        generatedClones,
                        structuralClones,
                        attachedClones));
                    foreach (var clone in generatedClones)
                    {
                        consumedHandles.Add(clone.Handle);
                    }

                    foreach (var clone in structuralClones)
                    {
                        consumedHandles.Add(clone.Handle);
                    }

                    foreach (var clone in attachedClones)
                    {
                        consumedHandles.Add(clone.Handle);
                    }
                }

                if (consumedHandles.Count > 0)
                {
                    // Register consumed clones BEFORE any rebind work: even a failed
                    // or ambiguous rebind must never fall through to per-rafter detach.
                    RoofGeneratedCopyPreCommandSnapshotService.RegisterConsumedWholeRoofClones(
                        consumedHandles);
                }

                if (pairs.Count == 0)
                {
                    return;
                }

                foreach (var pair in pairs)
                {
                    RoofGeneratedCopyPreCommandSnapshotService.RegisterWholeRoofCopyExpectation(
                        pair.NewOwner,
                        pair.OldOwner);
                }

                foreach (var pair in pairs)
                {
#if DEBUG
                    RoofGeneratedCopyLifecycleDiag.WriteWholeCopyDetect(
                        document.Editor,
                        pair.OldOwner,
                        pair.NewOwner,
                        pair.GeneratedClones.Count,
                        pair.StructuralClones.Count,
                        pair.AttachedManualClones.Count,
                        "matched",
                        isMirror);
#endif
                    if (!TryRebindPair(
                            document,
                            transaction,
                            pair,
                            appendedAnnotationIds,
                            out var stage,
                            out var generatedRebuilt,
                            out var structuralRebuilt,
                            out var attachedManualRebound,
                            out var annotationsRebuilt))
                    {
                        // Rollback: return without commit. The old owner is untouched;
                        // the consumed clones keep their inherited metadata and are
                        // excluded from the per-rafter services.
#if DEBUG
                        RoofGeneratedCopyLifecycleDiag.WriteWholeCopyRebind(
                            document.Editor,
                            pair.OldOwner,
                            pair.NewOwner,
                            generatedRebuilt,
                            structuralRebuilt,
                            attachedManualRebound,
                            annotationsRebuilt,
                            stage,
                            "fail",
                            isMirror);
#endif
                        return;
                    }

#if DEBUG
                    RoofGeneratedCopyLifecycleDiag.WriteWholeCopyRebind(
                        document.Editor,
                        pair.OldOwner,
                        pair.NewOwner,
                        generatedRebuilt,
                        structuralRebuilt,
                        attachedManualRebound,
                        annotationsRebuilt,
                        stage,
                        "ok",
                        isMirror);
#endif
                }

                transaction.Commit();
                RoofGeneratedCopyPreCommandSnapshotService.MarkWholeRoofCopyRebindSucceeded(
                    pairs.Select(pair => pair.NewOwner));
            }
        }
        catch (System.Exception)
        {
            // Silent internal maintenance — do not break native COPY/MIRROR UX.
        }
    }

    private static bool TryRebindPair(
        Document document,
        Transaction transaction,
        WholeRoofPair pair,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds,
        out string stage,
        out int generatedRebuilt,
        out int structuralRebuilt,
        out int attachedManualRebound,
        out int annotationsRebuilt)
    {
        stage = "start";
        generatedRebuilt = 0;
        structuralRebuilt = 0;
        attachedManualRebound = 0;
        annotationsRebuilt = 0;
        var database = document.Database;
        var newOwner = pair.NewOwnerCandidate;

        var cloneIds = pair.GeneratedClones
            .Select(clone => clone.Id)
            .ToArray();
        var structuralCloneIds = pair.StructuralClones
            .Select(clone => clone.Id)
            .ToArray();

#if DEBUG
        OriginAnnotationSnapshot? originAnnotationBefore = null;
#endif

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                newOwner.PolylineId,
                OpenMode.ForRead,
                out var ownerPolyline,
                database) ||
            ownerPolyline is null)
        {
            stage = "new-owner-polyline";
            return false;
        }

        // Recipe + reserved ElementIds are recovered from the clones BEFORE they are
        // erased; the copied definition carries suppression/overrides/edit state.
        // A roof whose ENTIRE generated set is suppressed has zero physical clones:
        // there is nothing to rebuild (the source has no rafters either), so the
        // generated step is skipped and the AttachedManual/display/group rebind still
        // completes — equivalent logical state.
        RoofRafterGenerationRecipe recipe = default!;
        if (cloneIds.Length > 0 &&
            !RoofGeneratedRafterSetService.TryRecoverRecipe(
                database,
                transaction,
                cloneIds,
                out recipe))
        {
            stage = "recipe-recovery";
            return false;
        }

        RoofRafterLayout? layout = null;
        IReadOnlyDictionary<RoofGeneratedMemberKey, string>? reservedElementIds = null;
        if (cloneIds.Length > 0)
        {
            reservedElementIds = RoofGeneratedRafterSetService.CollectReservedElementIds(
                database,
                transaction,
                cloneIds,
                newOwner.Definition);

            var layoutResult = RoofRafterLayoutSolver.Solve(
                newOwner.Geometry,
                AutoCadRoofRafterSpacingStore.CreateLayoutParameters(
                    database,
                    recipe.MaximumSpacingMm,
                    recipe.WidthMm));
            if (!layoutResult.IsValid || layoutResult.Layout is null)
            {
                stage = "layout";
                return false;
            }

            layout = layoutResult.Layout;
        }

        HipRoofGeometry? structuralGeometry = null;
        if (structuralCloneIds.Length > 0)
        {
            structuralGeometry = newOwner.Geometry as HipRoofGeometry;
            if (structuralGeometry is null)
            {
                stage = "structural-hip-geometry";
                return false;
            }
        }

        if (cloneIds.Length > 0 || structuralCloneIds.Length > 0)
        {
            // Stale cloned display lines (inherited old-owner reference, not in the
            // pre-command display set) are removed so the old owner's display cannot be
            // duplicated and the new owner's display rebuilds cleanly.
            EraseStaleDisplayClones(document, transaction, pair.OldOwner);

            // Stale cloned annotations bound to the old owner's timber handles: removed
            // by command-lifecycle identity (appended during THIS command), never by
            // source handle alone (ORIGINAL annotations share the same source handles).
            // AutoCAD may also erase+recreate source MLeaders into the appended set;
            // those sole survivors must be kept (peer-gated consume).
#if DEBUG
            // Baseline MUST exclude native MIRROR ObjectAppended clones that still carry
            // old SourceHandles (otherwise beforeCount = 2 × real origin annotations).
            originAnnotationBefore = CaptureOriginAnnotationSnapshot(
                database,
                transaction,
                pair.OldOwner,
                appendedAnnotationIds);
#endif
            var staleAnnotationClonesRemoved = EraseStaleAnnotationClones(
                document,
                transaction,
                appendedAnnotationIds,
                pair.OldOwner);

            // Transient generated clones are erased (detach-before-erase is not
            // required: native COPY does not clone groups, so the clones are not group
            // members).
            EraseGeneratedClones(document, transaction, cloneIds);
            EraseGeneratedClones(document, transaction, structuralCloneIds);

            // RefreshTimberElements (which runs BEFORE this branch) already created a
            // canonical annotation set bound to the TEMPORARY clone handles C1..Cn.
            // With the clones now erased, those annotations are orphans: delete them by
            // the deterministic command-scoped identity SourceHandle ∈ THIS pair's
            // temporary generated clone handles. DeleteForMissingSourceHandles requires
            // the source handle to be missing/erased — exactly the state after
            // EraseGeneratedClones — and can never touch original (live O) handles,
            // final regenerated handles (N do not exist yet) or AttachedManual handles.
            var temporaryCloneHandles = pair.GeneratedClones
                .Select(clone => clone.Handle)
                .Concat(pair.StructuralClones.Select(clone => clone.Handle))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var temporaryCloneOrphanAnnotations = CountAnnotationsBoundToHandles(
                database,
                transaction,
                temporaryCloneHandles);
            TimberAnnotationService.DeleteForMissingSourceHandles(
                database,
                transaction,
                temporaryCloneHandles);
            annotationsRebuilt = staleAnnotationClonesRemoved + temporaryCloneOrphanAnnotations;

            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(ownerPolyline);
            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var layerProfile = ElementLayerProfileStore.Load();

            // Whole-roof COPY/MIRROR: rebuild the new owner's display assembly BEFORE
            // ordinary/structural materialization. Canonical TrySyncForOwner requires
            // display children; during rebind the old owner's display clones were just
            // erased and the new owner is incomplete until Rebuild. Defer intermediate
            // group sync inside Materialize*/structural — one final TrySyncForOwner
            // runs after the complete new assembly exists (below).
            var edges = RoofWireframe.Create(newOwner.Geometry, sourceElevation);
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            if (!RoofDisplayService.Rebuild(
                    database,
                    transaction,
                    newOwner.PolylineId,
                    newOwner.Handle,
                    edges,
                    signature))
            {
                stage = "display";
                return false;
            }

            if (layout is not null)
            {
                var created = RoofGeneratedRafterSetService.Materialize(
                    database,
                    transaction,
                    document.Editor,
                    ownerPolyline,
                    newOwner.Handle,
                    newOwner.Geometry,
                    layout,
                    recipe,
                    defaultProfile,
                    layerProfile,
                    reservedElementIds,
                    syncAssemblyGroup: false);
                generatedRebuilt = created.Count;
            }

            if (structuralGeometry is not null)
            {
                // Mirrored owners deep-clone BoundaryIdentity with the pre-mirror
                // RawWinding. Re-home from the CURRENT mirrored footprint before
                // structural materialization so ValidateCurrentSource can pass
                // without weakening CurrentRawWindingMismatch. COPY leaves a
                // still-valid identity untouched (Existing). Original owner is
                // never opened here.
                var boundaryRehome = RoofBoundaryIdentityService.RehomeForCurrentSource(
                    database,
                    transaction,
                    newOwner.PolylineId);
                if (boundaryRehome.Identity is null)
                {
                    stage = "boundary-identity-" + boundaryRehome.Error;
                    return false;
                }

                var structural =
                    RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(
                        document,
                        transaction,
                        ownerPolyline,
                        newOwner.Handle,
                        RoofPolylineExtractor.Extract(ownerPolyline),
                        structuralGeometry,
                        defaultProfile,
                        layerProfile,
                        syncAssemblyGroup: false);
                if (!structural.IsSuccess)
                {
                    stage = "structural-" + structural.Result;
                    return false;
                }

                structuralRebuilt = structural.Actual;
            }
        }

        foreach (var clone in pair.AttachedManualClones)
        {
            if (!TryRebindAttachedManualClone(
                    document,
                    transaction,
                    clone,
                    pair.NewOwner,
                    out var cloneStage))
            {
                stage = $"attached-manual-{cloneStage}";
                return false;
            }

            attachedManualRebound++;
        }

        // Whole-roof MIRROR deep-clones definition XData with the pre-mirror
        // RigidFootprint orientation. CollectOwners already accepts that flip via
        // Classify(RigidEquivalent), but final TrySyncForOwner →
        // TryCollectCurrentStructuralDisplayChildIds → TryGetExpectedDisplay uses
        // strict Restore only. Persist the live mirrored geometry so Restore
        // succeeds, EnsureGroup can expand the display-only group (owner+display)
        // to the full assembly, and DissociateOwnerFromForeignGroups can dissolve
        // any native MIRROR group clones. COPY translation already Matches Restore
        // and skips this write. Original owner is never opened here.
        if (!TryPersistMirroredOwnerDefinitionForGroupSync(
                transaction,
                ownerPolyline,
                newOwner.Geometry,
                out var definitionStage))
        {
            stage = definitionStage;
            return false;
        }

        RoofUnlockIndicatorService.Sync(database, transaction, ownerPolyline);
        // Selectability + foreign-group prune only. Membership repair must NOT run here:
        // TrySyncForOwner below is the single full EnsureGroup for this rebind. A prior
        // repair EnsureGroup after display-only Rebuild can Append timber/annotation
        // ObjectIds twice (AutoCAD Group allows duplicate slots) → SAVE/REOPEN 598.
        RoofDisplayGroupSelectabilityService.ApplyForOwner(
            database,
            transaction,
            newOwner.PolylineId,
            repairMembership: false);
        if (!RoofAssemblyGroupSyncService.TrySyncForOwner(
                document,
                transaction,
                newOwner.PolylineId))
        {
            stage = "group-sync";
            return false;
        }

#if DEBUG
        if (originAnnotationBefore is not null)
        {
            // After cleanup, clones are erased; still pass the appended set so any
            // surviving sole-appended recreate is classified via the same exclude rule
            // only when it was NOT in the pre-cleanup baseline (recreates change handles).
            EmitOriginMirrorAnnotationInvariant(
                document,
                database,
                transaction,
                pair.OldOwner,
                originAnnotationBefore,
                appendedAnnotationIds);
        }
#endif

#if DEBUG
        RoofGroupPersistenceDiag.Write(
            document.Editor,
            database,
            transaction,
            newOwner.PolylineId,
            "post-whole-roof-rebind-sync");
#endif

        RoofDisplayService.EnsureAllDisplayBehindTimber(database, transaction);

        stage = "complete";
        return true;
    }

    /// <summary>
    /// When the new owner's live footprint no longer Matches the deep-cloned
    /// RigidFootprint (whole-roof MIRROR orientation flip), rewrite definition
    /// geometry from the already-solved rebind geometry so strict Restore used by
    /// group sync can succeed. No-op when Restore already Matches (COPY / already
    /// rewritten). Never touches the original owner.
    /// </summary>
    private static bool TryPersistMirroredOwnerDefinitionForGroupSync(
        Transaction transaction,
        Polyline ownerPolyline,
        IRoofGeometry geometry,
        out string stage)
    {
        stage = "definition-persist";
        var input = RoofPolylineExtractor.Extract(ownerPolyline);
        var validation = RoofFootprintValidator.Validate(input);
        var stored = RoofDefinitionStore.Read(ownerPolyline);
        if (!validation.IsValid ||
            validation.Footprint is null ||
            stored.Data is null)
        {
            stage = "definition-read";
            return false;
        }

        var restored = RoofDefinitionPersistence.Restore(
            input,
            validation.Footprint,
            stored.Data);
        if (restored.IsValid)
        {
            // COPY / already-current definition: Restore Matches — no write.
            return true;
        }

        if (geometry is not HipRoofGeometry hipGeometry)
        {
            stage = "definition-hip-geometry";
            return false;
        }

        // Classify must still accept the flip as RigidEquivalent/SupportedResize so
        // we never rewrite a true Unsupported footprint during rebind.
        var classified = RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
        if (classified.Geometry is null ||
            classified.Kind is not (
                RoofSourceChangeKind.RigidEquivalent or
                RoofSourceChangeKind.SupportedResize))
        {
            stage = "definition-classify";
            return false;
        }

        var updated = RoofDefinitionPersistence.UpdateGeometry(
            stored.Data,
            input,
            hipGeometry);
        if (!ownerPolyline.IsWriteEnabled)
        {
            ownerPolyline.UpgradeOpen();
        }

        RoofDefinitionStore.Write(ownerPolyline, transaction, updated);

        var verify = RoofDefinitionPersistence.Restore(
            input,
            validation.Footprint,
            updated);
        if (!verify.IsValid)
        {
            stage = "definition-verify";
            return false;
        }

        return true;
    }

    private static bool TryRebindAttachedManualClone(
        Document document,
        Transaction transaction,
        AppendedAttachedClone clone,
        string newOwner,
        out string stage)
    {
        stage = "start";
        if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                transaction,
                clone.Id,
                OpenMode.ForWrite,
                out var cloneLine,
                document.Database) ||
            cloneLine is null)
        {
            stage = "read-clone";
            return false;
        }

        var data = clone.Data;
        var anchorKey = data.AnchorGeneratedMemberKey;
        if (anchorKey is null)
        {
            // Legacy child without a logical anchor: rebind ownership + identity only.
            // Relative geometry semantics stay exactly as persisted on the source roof.
            RoofAttachedManualTimberStore.Write(
                cloneLine,
                transaction,
                data with
                {
                    SchemaVersion = RoofAttachedManualTimberDataSchema.CurrentVersion,
                    RoofOwnerReference = newOwner,
                    ChildIdentity = clone.Handle,
                });
            stage = "rebound-owner-only";
            return true;
        }

        if (RoofAttachedManualLifecycleService.TryFindGeneratedAnchorLine(
                document.Database,
                transaction,
                newOwner,
                anchorKey.Value,
                out var anchorLine) &&
            anchorLine is not null)
        {
            // Deterministic logical-key anchor resolution against the NEW owner's
            // regenerated set — never nearest-roof or nearest-rafter guessing.
            var anchored = RoofAttachedManualLifecycleService.CreateAnchoredData(
                newOwner,
                clone.Handle,
                anchorKey.Value,
                anchorLine.StartPoint,
                anchorLine.EndPoint,
                cloneLine.StartPoint,
                cloneLine.EndPoint,
                data.Origin);
            RoofAttachedManualLifecycleService.WriteAnchored(cloneLine, transaction, anchored);
        }
        else
        {
            // Anchor station absent in the NEW roof (e.g. a suppressed station): keep
            // the persisted anchor/relative state, rebind ownership + identity, and
            // leave the entity visibility exactly as copied — dormancy semantics are
            // preserved, never force-activated.
            RoofAttachedManualTimberStore.Write(
                cloneLine,
                transaction,
                data with
                {
                    SchemaVersion = RoofAttachedManualTimberDataSchema.CurrentVersion,
                    RoofOwnerReference = newOwner,
                    ChildIdentity = clone.Handle,
                });
        }

        // The clone's WCS is authoritative for its annotation; a dormant (hidden)
        // child stays without an annotation exactly like the source.
        if (cloneLine.Visible)
        {
            _ = ElementLabelService.UpdateInCurrentTransaction(
                document.Database,
                transaction,
                document.Editor,
                new[] { clone.Id },
                new[] { clone.Id });
        }

        stage = "rebound";
        return true;
    }

    private static IReadOnlyList<OwnerCandidate> CollectOwners(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var owners = new List<OwnerCandidate>();
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Polyline polyline ||
                polyline.IsErased)
            {
                continue;
            }

            var stored = RoofDefinitionStore.Read(polyline);
            if (stored.Data is null)
            {
                continue;
            }

            var input = RoofPolylineExtractor.Extract(polyline);
            var validation = RoofFootprintValidator.Validate(input);
            if (!validation.IsValid || validation.Footprint is null)
            {
                continue;
            }

            var restored = RoofDefinitionPersistence.Restore(
                input,
                validation.Footprint,
                stored.Data);
            // Strict Hip Restore rejects a mirrored polyline that still carries the
            // pre-mirror RigidFootprint orientation (verbatim deep-clone XData). The
            // live Classify path already accepts that pure winding flip as
            // RigidEquivalent — reuse it so whole-roof MIRROR can see the new owner
            // before (or without) LiveResize rewriting the definition.
            IRoofGeometry? geometry = restored.IsValid ? restored.Geometry : null;
            if (geometry is null)
            {
                var classified = RoofDefinitionPersistence.Classify(
                    input,
                    validation.Footprint,
                    stored.Data);
                if (classified.Geometry is null ||
                    classified.Kind is not (
                        RoofSourceChangeKind.RigidEquivalent or
                        RoofSourceChangeKind.SupportedResize))
                {
                    continue;
                }

                geometry = classified.Geometry;
            }

            owners.Add(new OwnerCandidate(
                polyline.Handle.ToString(),
                polyline.ObjectId,
                geometry,
                stored.Data));
        }

        return owners;
    }

    private static IReadOnlyList<AppendedGeneratedClone> CollectAppendedGeneratedClones(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        var clones = new List<AppendedGeneratedClone>(appendedTimberIds.Count);
        foreach (var id in appendedTimberIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line);
            if (generated.Data is null ||
                generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter)
            {
                continue;
            }

            clones.Add(new AppendedGeneratedClone(
                line.Handle.ToString(),
                id,
                generated.Data.RoofOwnerReference));
        }

        return clones;
    }

    private static IReadOnlyList<AppendedStructuralClone> CollectAppendedStructuralClones(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        var clones = new List<AppendedStructuralClone>(appendedTimberIds.Count);
        foreach (var id in appendedTimberIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var structural = RoofStructuralGeneratedStore.Read(line);
            if (structural.Data is null)
            {
                continue;
            }

            clones.Add(new AppendedStructuralClone(
                line.Handle.ToString(),
                id,
                structural.Data));
        }

        return clones;
    }

    private static IReadOnlyList<AppendedAttachedClone> CollectAppendedAttachedClones(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        var clones = new List<AppendedAttachedClone>(appendedTimberIds.Count);
        foreach (var id in appendedTimberIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null)
            {
                continue;
            }

            var attached = RoofAttachedManualTimberStore.Read(line);
            if (attached.Data is null)
            {
                continue;
            }

            clones.Add(new AppendedAttachedClone(
                line.Handle.ToString(),
                id,
                attached.Data));
        }

        return clones;
    }

    private static void EraseGeneratedClones(
        Document document,
        Transaction transaction,
        IReadOnlyList<ObjectId> cloneIds)
    {
        foreach (var id in cloneIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var entity,
                    document.Database) ||
                entity is null ||
                entity.IsErased)
            {
                continue;
            }

            entity.Erase();
        }
    }

    private static int EraseStaleAnnotationClones(
        Document document,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds,
        string oldOwner)
    {
        if (appendedAnnotationIds is null || appendedAnnotationIds.Count == 0)
        {
            return 0;
        }

        var preTimberHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandGeneratedHandlesByOwner(oldOwner))
        {
            preTimberHandles.Add(handle);
        }

        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandStructuralGeneratedHandlesByOwner(oldOwner))
        {
            preTimberHandles.Add(handle);
        }

        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandAttachedManualHandlesByOwner(oldOwner))
        {
            preTimberHandles.Add(handle);
        }

        if (preTimberHandles.Count == 0)
        {
            return 0;
        }

        var appendedSet = new HashSet<ObjectId>(appendedAnnotationIds);
        var livingPeerRoles = CollectLivingNonAppendedAnnotationRoles(
            document.Database,
            transaction,
            appendedSet);
        var soleSurvivorKeepIds = SelectSoleSurvivorKeepIds(
            document.Database,
            transaction,
            appendedAnnotationIds,
            preTimberHandles,
            livingPeerRoles);

        var erased = 0;
        foreach (var id in appendedAnnotationIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null)
            {
                continue;
            }

            // Clone annotations deep-copy source XData → SOURCE timber handle.
            // Appended + bound to old-owner pre-command timber is necessary but not
            // sufficient: AutoCAD may erase+recreate a source MLeader into the same
            // appended set. Erase true clones when a living non-appended peer exists;
            // when only appended survivors remain, keep exactly one per role.
            if (!RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) ||
                !preTimberHandles.Contains(sourceHandle) ||
                !TryResolveAnnotationConsumeRole(entity, out var roleKey))
            {
                continue;
            }

            var peerKey = sourceHandle.Trim() + "|" + roleKey;
            var hasPeer = livingPeerRoles.Contains(peerKey);
            var keepAsSoleSurvivor = soleSurvivorKeepIds.Contains(id);
            var shouldErase = hasPeer
                ? RoofMirrorAnnotationConsumeRules.ShouldEraseAppendedAnnotationClone(true)
                : !keepAsSoleSurvivor;
#if DEBUG
            TraceMirrorAnnotationConsume(
                document,
                oldOwner,
                id,
                entity,
                sourceHandle,
                hasPeer,
                shouldErase,
                keepAsSoleSurvivor);
#endif
            if (!shouldErase)
            {
                continue;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var writable,
                    document.Database) &&
                writable is not null)
            {
                writable.Erase();
                erased++;
            }
        }

        return erased;
    }

    /// <summary>
    /// When AutoCAD both clones and recreates a source annotation, both ObjectIds are
    /// appended and no non-appended peer remains. Keep the lowest-handle survivor per
    /// SourceHandle+role; erase the other appended duplicates.
    /// </summary>
    private static HashSet<ObjectId> SelectSoleSurvivorKeepIds(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds,
        IReadOnlySet<string> preTimberHandles,
        IReadOnlySet<string> livingPeerRoles)
    {
        var keep = new HashSet<ObjectId>();
        var bestByRole = new Dictionary<string, (ObjectId Id, long HandleValue)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in appendedAnnotationIds)
        {
            if (id.IsNull ||
                id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) ||
                !preTimberHandles.Contains(sourceHandle) ||
                !TryResolveAnnotationConsumeRole(entity, out var roleKey))
            {
                continue;
            }

            var peerKey = sourceHandle.Trim() + "|" + roleKey;
            if (livingPeerRoles.Contains(peerKey))
            {
                continue;
            }

            var handleValue = entity.Handle.Value;
            if (!bestByRole.TryGetValue(peerKey, out var best) || handleValue < best.HandleValue)
            {
                bestByRole[peerKey] = (id, handleValue);
            }
        }

        foreach (var entry in bestByRole.Values)
        {
            keep.Add(entry.Id);
        }

        return keep;
    }

    private static HashSet<string> CollectLivingNonAppendedAnnotationRoles(
        Database database,
        Transaction transaction,
        IReadOnlySet<ObjectId> appendedSet)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                appendedSet.Contains(id) ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased ||
                !RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) ||
                !TryResolveAnnotationConsumeRole(entity, out var roleKey))
            {
                continue;
            }

            roles.Add(sourceHandle.Trim() + "|" + roleKey);
        }

        return roles;
    }

    private static bool TryResolveAnnotationConsumeRole(Entity entity, out string roleKey)
    {
        roleKey = string.Empty;
        if (ElementLabelStore.TryRead(entity, out var label) && label is not null)
        {
            roleKey = RoofMirrorAnnotationConsumeRules.FormatMainLabelRole(
                label.ComponentRole.ToString());
            return true;
        }

        if (SlopeArrowStore.TryRead(entity, out var arrow) && arrow is not null)
        {
            roleKey = RoofMirrorAnnotationConsumeRules.RoleSlopeArrow;
            return true;
        }

        if (SlopeAngleTextStore.TryRead(entity, out var angle) && angle is not null)
        {
            roleKey = RoofMirrorAnnotationConsumeRules.RoleSlopeAngle;
            return true;
        }

        if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
            post is not null)
        {
            roleKey = RoofMirrorAnnotationConsumeRules.RolePostPerpendicular;
            return true;
        }

        return false;
    }

#if DEBUG
    private sealed record OriginAnnotationSnapshot(
        int Count,
        IReadOnlyDictionary<string, string> SourceHandleByAnnotationHandle);

    private static OriginAnnotationSnapshot CaptureOriginAnnotationSnapshot(
        Database database,
        Transaction transaction,
        string oldOwner,
        IReadOnlyCollection<ObjectId>? excludeAppendedAnnotationIds)
    {
        var timberHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandGeneratedHandlesByOwner(oldOwner))
        {
            timberHandles.Add(handle);
        }

        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandStructuralGeneratedHandlesByOwner(oldOwner))
        {
            timberHandles.Add(handle);
        }

        foreach (var handle in RoofGeneratedCopyPreCommandSnapshotService
                     .GetPreCommandAttachedManualHandlesByOwner(oldOwner))
        {
            timberHandles.Add(handle);
        }

        var excludeAppended = excludeAppendedAnnotationIds is null
            ? new HashSet<ObjectId>()
            : new HashSet<ObjectId>(excludeAppendedAnnotationIds);
        var candidates = new List<RoofMirrorOriginAnnotationCandidate>();
        var appendedKeys = new List<string>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased ||
                !RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) ||
                !timberHandles.Contains(sourceHandle))
            {
                continue;
            }

            var annotationKey = entity.Handle.ToString();
            candidates.Add(new RoofMirrorOriginAnnotationCandidate
            {
                AnnotationKey = annotationKey,
                SourceHandle = sourceHandle.Trim(),
            });
            if (excludeAppended.Contains(id))
            {
                appendedKeys.Add(annotationKey);
            }
        }

        var originKeys = new HashSet<string>(
            RoofMirrorAnnotationConsumeRules.SelectOriginAnnotationKeysExcludingAppended(
                candidates,
                timberHandles,
                appendedKeys),
            StringComparer.OrdinalIgnoreCase);
        var byHandle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!originKeys.Contains(candidate.AnnotationKey))
            {
                continue;
            }

            byHandle[candidate.AnnotationKey] = candidate.SourceHandle;
        }

        return new OriginAnnotationSnapshot(byHandle.Count, byHandle);
    }

    private static void EmitOriginMirrorAnnotationInvariant(
        Document document,
        Database database,
        Transaction transaction,
        string oldOwner,
        OriginAnnotationSnapshot before,
        IReadOnlyCollection<ObjectId>? appendedAnnotationIds)
    {
        // Post-rebind: exclude any still-living appended ObjectIds (normally erased).
        var after = CaptureOriginAnnotationSnapshot(
            database,
            transaction,
            oldOwner,
            appendedAnnotationIds);
        var missing = before.SourceHandleByAnnotationHandle.Keys
            .Where(handle => !after.SourceHandleByAnnotationHandle.ContainsKey(handle))
            .OrderBy(handle => handle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extra = after.SourceHandleByAnnotationHandle.Keys
            .Where(handle => !before.SourceHandleByAnnotationHandle.ContainsKey(handle))
            .OrderBy(handle => handle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var changedSource = before.SourceHandleByAnnotationHandle
            .Where(pair =>
                after.SourceHandleByAnnotationHandle.TryGetValue(pair.Key, out var current) &&
                !string.Equals(current, pair.Value, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .OrderBy(handle => handle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var erased = missing
            .Where(handle =>
            {
                try
                {
                    var id = database.GetObjectId(
                        false,
                        new Handle(Convert.ToInt64(handle, 16)),
                        0);
                    return id.IsNull || id.IsErased;
                }
                catch
                {
                    return true;
                }
            })
            .ToArray();

        // Exact ObjectId/handle identity is preferred. AutoCAD may erase+recreate
        // source MLeaders (new handles) while preserving SourceHandle bindings —
        // treat equal counts + equal SourceHandle multiset as logical pass.
        var beforeSources = before.SourceHandleByAnnotationHandle.Values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var afterSources = after.SourceHandleByAnnotationHandle.Values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceMultisetEqual = beforeSources.Length == afterSources.Length &&
            beforeSources.SequenceEqual(afterSources, StringComparer.OrdinalIgnoreCase);
        var exactIdentity = missing.Length == 0 &&
            extra.Length == 0 &&
            changedSource.Length == 0 &&
            after.Count == before.Count;
        var logicalIdentity = after.Count == before.Count &&
            changedSource.Length == 0 &&
            sourceMultisetEqual;
        var ok = exactIdentity || logicalIdentity;
        try
        {
            document.Editor?.WriteMessage(
                "\nORIGIN_MIRROR_ANNOTATION_INVARIANT" +
                $" owner={oldOwner}" +
                $" beforeCount={before.Count}" +
                $" afterCount={after.Count}" +
                $" missingHandles={(missing.Length == 0 ? "-" : string.Join(",", missing))}" +
                $" extraHandles={(extra.Length == 0 ? "-" : string.Join(",", extra))}" +
                $" changedSourceHandles={(changedSource.Length == 0 ? "-" : string.Join(",", changedSource))}" +
                $" changedOwners=-" +
                $" erasedHandles={(erased.Length == 0 ? "-" : string.Join(",", erased))}" +
                $" identity={(exactIdentity ? "preserved" : logicalIdentity ? "logical" : "broken")}" +
                $" result={(ok ? "ok" : "fail")}");
        }
        catch
        {
        }
    }

    private static void TraceMirrorAnnotationConsume(
        Document document,
        string oldOwner,
        ObjectId objectId,
        Entity entity,
        string sourceHandle,
        bool hasPeer,
        bool shouldErase,
        bool keepAsSoleSurvivor)
    {
        try
        {
            var classification = hasPeer
                ? RoofMirrorAnnotationConsumeRules.ClassifyAppendedAnnotation(true)
                : keepAsSoleSurvivor
                    ? "source-original"
                    : "mirrored-clone";
            var action = shouldErase ? "erase" : "keep";
            var reason = hasPeer
                ? "living-non-appended-peer"
                : keepAsSoleSurvivor
                    ? "sole-appended-survivor"
                    : "surplus-appended-duplicate";
            document.Editor?.WriteMessage(
                "\nROOF_MIRROR_ANNOTATION_CONSUME" +
                $" oldOwner={oldOwner}" +
                $" newOwner=-" +
                $" objectId={objectId}" +
                $" handle={entity.Handle}" +
                $" sourceHandle={sourceHandle}" +
                $" classification={classification}" +
                $" action={action}" +
                $" reason={reason}");
        }
        catch
        {
        }
    }
#endif

    private static int CountAnnotationsBoundToHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> handles)
    {
        if (handles is null || handles.Count == 0)
        {
            return 0;
        }

        var target = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        var count = 0;
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased)
            {
                continue;
            }

            if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) &&
                target.Contains(sourceHandle))
            {
                count++;
            }
        }

        return count;
    }

    private static void EraseStaleDisplayClones(
        Document document,
        Transaction transaction,
        string oldOwner)
    {
        var preDisplayHandles = new HashSet<string>(
            RoofGeneratedCopyPreCommandSnapshotService.GetPreCommandDisplayHandlesByOwner(oldOwner),
            StringComparer.OrdinalIgnoreCase);
        var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    document.Database) ||
                line is null)
            {
                continue;
            }

            var display = RoofDisplayStore.Read(line);
            if (display.Data is null ||
                !string.Equals(
                    display.Data.OwnerReference,
                    oldOwner,
                    StringComparison.OrdinalIgnoreCase) ||
                preDisplayHandles.Contains(line.Handle.ToString()))
            {
                continue;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var writable,
                    document.Database) &&
                writable is not null)
            {
                writable.Erase();
            }
        }
    }
}
