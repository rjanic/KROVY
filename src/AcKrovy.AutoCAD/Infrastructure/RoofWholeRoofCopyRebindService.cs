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
/// Deterministic whole-roof same-DWG COPY lifecycle.
/// Detection is payload/event-based (never spatial): a RoofDefinition-bearing source
/// Polyline that appeared in THIS COPY command, paired with its pre-command owner by
/// decoded RoofDefinition payload equality, whose complete CURRENT physical owned
/// assembly (generated + AttachedManual) was appended with the inherited old-owner
/// metadata. The branch erases the transient generated clones, regenerates the
/// canonical set under the new owner through the shared Generated pipeline, rebinds
/// AttachedManual clones by their logical anchor key, rebuilds display/annotations
/// and syncs the canonical group. A detected whole-roof set is registered as consumed
/// BEFORE any work so the per-rafter services can never detach it under the old owner
/// — including when the rebind itself fails (transaction rolls back, old owner stays
/// untouched, no old-owner AttachedManual conversion).
/// </summary>
internal static class RoofWholeRoofCopyRebindService
{
    private sealed record OwnerCandidate(
        string Handle,
        ObjectId PolylineId,
        SimpleGableRoofGeometry Geometry,
        RoofDefinitionData? Definition);

    private sealed record AppendedGeneratedClone(string Handle, ObjectId Id, string OwnerReference);

    private sealed record AppendedAttachedClone(string Handle, ObjectId Id, RoofAttachedManualTimberData Data);

    private sealed record WholeRoofPair(
        string OldOwner,
        string NewOwner,
        OwnerCandidate NewOwnerCandidate,
        IReadOnlyList<AppendedGeneratedClone> GeneratedClones,
        IReadOnlyList<AppendedAttachedClone> AttachedManualClones);

    public static void Process(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> appendedTimberIds,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds)
    {
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName) ||
            !LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName) ||
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
                    return;
                }

                var appendedGenerated = CollectAppendedGeneratedClones(
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
                        var preAttached = RoofGeneratedCopyPreCommandSnapshotService
                            .GetPreCommandAttachedManualHandlesByOwner(owner.Handle);
                        return RoofWholeRoofCopyIdentityRules.IsCompleteAssemblyClone(
                            preGenerated.Count,
                            preAttached.Count,
                            appendedGenerated.Count(clone => string.Equals(
                                clone.OwnerReference,
                                owner.Handle,
                                StringComparison.OrdinalIgnoreCase)),
                            appendedAttached.Count(clone => string.Equals(
                                clone.Data.RoofOwnerReference,
                                owner.Handle,
                                StringComparison.OrdinalIgnoreCase)));
                    })
                    .ToArray();

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
                        // (or its definition is unreadable): not a whole-roof copy —
                        // the ordinary per-rafter path keeps its existing semantics.
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
                        foreach (var clone in ambiguousGenerated)
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
                            ambiguousAttached.Length,
                            "ambiguous");
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
                            "ambiguous");
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
                    pairs.Add(new WholeRoofPair(
                        oldOwner.Handle,
                        newOwner.Handle,
                        newOwner,
                        generatedClones,
                        attachedClones));
                    foreach (var clone in generatedClones)
                    {
                        consumedHandles.Add(clone.Handle);
                    }

                    foreach (var clone in attachedClones)
                    {
                        consumedHandles.Add(clone.Handle);
                    }
                }

                if (pairs.Count == 0)
                {
                    return;
                }

                // Register consumed clones BEFORE any rebind work: even a failed rebind
                // must never fall through to per-rafter detach under the old owner.
                RoofGeneratedCopyPreCommandSnapshotService.RegisterConsumedWholeRoofClones(
                    consumedHandles);

                foreach (var pair in pairs)
                {
#if DEBUG
                    RoofGeneratedCopyLifecycleDiag.WriteWholeCopyDetect(
                        document.Editor,
                        pair.OldOwner,
                        pair.NewOwner,
                        pair.GeneratedClones.Count,
                        pair.AttachedManualClones.Count,
                        "matched");
#endif
                    if (!TryRebindPair(
                            document,
                            transaction,
                            pair,
                            appendedAnnotationIds,
                            out var stage,
                            out var generatedRebuilt,
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
                            attachedManualRebound,
                            annotationsRebuilt,
                            stage,
                            "fail");
#endif
                        return;
                    }

#if DEBUG
                    RoofGeneratedCopyLifecycleDiag.WriteWholeCopyRebind(
                        document.Editor,
                        pair.OldOwner,
                        pair.NewOwner,
                        generatedRebuilt,
                        attachedManualRebound,
                        annotationsRebuilt,
                        stage,
                        "ok");
#endif
                }

                transaction.Commit();
            }
        }
        catch (System.Exception)
        {
            // Silent internal maintenance — do not break native COPY UX.
        }
    }

    private static bool TryRebindPair(
        Document document,
        Transaction transaction,
        WholeRoofPair pair,
        IReadOnlyCollection<ObjectId> appendedAnnotationIds,
        out string stage,
        out int generatedRebuilt,
        out int attachedManualRebound,
        out int annotationsRebuilt)
    {
        stage = "start";
        generatedRebuilt = 0;
        attachedManualRebound = 0;
        annotationsRebuilt = 0;
        var database = document.Database;
        var newOwner = pair.NewOwnerCandidate;

        var cloneIds = pair.GeneratedClones
            .Select(clone => clone.Id)
            .ToArray();

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

        if (cloneIds.Length > 0)
        {
            var reservedElementIds = RoofGeneratedRafterSetService.CollectReservedElementIds(
                database,
                transaction,
                cloneIds,
                newOwner.Definition);

            var layoutResult = SimpleGableRafterLayoutSolver.Solve(
                newOwner.Geometry,
                new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm));
            if (!layoutResult.IsValid || layoutResult.Layout is null)
            {
                stage = "layout";
                return false;
            }

            // Stale cloned display lines (inherited old-owner reference, not in the
            // pre-command display set) are removed so the old owner's display cannot be
            // duplicated and the new owner's display rebuilds cleanly.
            EraseStaleDisplayClones(document, transaction, pair.OldOwner);

            // Stale cloned annotations bound to the old owner's timber handles: removed
            // by command-lifecycle identity (appended during THIS command), never by
            // source handle (the ORIGINAL annotations share the same source handles).
            var staleAnnotationClonesRemoved = EraseStaleAnnotationClones(
                document,
                transaction,
                appendedAnnotationIds,
                pair.OldOwner);

            // Transient generated clones are erased (detach-before-erase is not
            // required: native COPY does not clone groups, so the clones are not group
            // members).
            EraseGeneratedClones(document, transaction, cloneIds);

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
            var created = RoofGeneratedRafterSetService.Materialize(
                database,
                transaction,
                document.Editor,
                ownerPolyline,
                newOwner.Handle,
                newOwner.Geometry,
                layoutResult.Layout,
                recipe,
                TimberElementDefaultProfileStore.Load(),
                ElementLayerProfileStore.Load(),
                reservedElementIds);
            generatedRebuilt = created.Count;

            var edges = SimpleGableRoofWireframe.Create(newOwner.Geometry, sourceElevation);
            var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
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

        RoofUnlockIndicatorService.Sync(database, transaction, ownerPolyline);
        RoofDisplayGroupSelectabilityService.ApplyForOwner(
            database,
            transaction,
            newOwner.PolylineId);
        _ = RoofAssemblyGroupSyncService.TrySyncForOwner(
            document,
            transaction,
            newOwner.PolylineId);

        stage = "complete";
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
            if (!restored.IsValid || restored.Geometry is null)
            {
                continue;
            }

            owners.Add(new OwnerCandidate(
                polyline.Handle.ToString(),
                polyline.ObjectId,
                restored.Geometry,
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
                     .GetPreCommandAttachedManualHandlesByOwner(oldOwner))
        {
            preTimberHandles.Add(handle);
        }

        if (preTimberHandles.Count == 0)
        {
            return 0;
        }

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

            // The clone annotation deep-copies the source's XData, so it resolves to a
            // SOURCE timber handle. Being in the appended set AND bound to a handle of
            // the old owner's pre-command assembly is the deterministic marker of a
            // stale COPY clone — the ORIGINAL annotations share the source handles but
            // are not appended, so they are never touched.
            if (!RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(
                    entity,
                    out var sourceHandle) ||
                !preTimberHandles.Contains(sourceHandle))
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
