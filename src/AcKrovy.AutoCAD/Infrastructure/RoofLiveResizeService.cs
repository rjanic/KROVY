using System.Reflection;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Live roof source resize and display-cache repair on the existing command-boundary
/// geometry path. Does not add a roof reactor, overrule, or deep-clone hook.
/// </summary>
internal static class RoofLiveResizeService
{
    private const BindingFlags ComInvoke =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Owners that already received SOURCE SupportedResize / Unsupported in the current
    /// STRETCH / GRIP_STRETCH command scope. Display rebuild side-effects must not be
    /// reinterpreted as independent display-only tamper for these owners.
    /// Cleared on the next CommandWillStart / cancel / fail boundary.
    /// </summary>
    private static readonly HashSet<ObjectId> SourceHandledOwnersThisCommand = new();

    private static readonly HashSet<ObjectId> SourceSupportedResizeOwnersThisCommand = new();

    public static void BeginStretchCommandScope()
    {
        SourceHandledOwnersThisCommand.Clear();
        SourceSupportedResizeOwnersThisCommand.Clear();
    }

    public static void EndStretchCommandScope()
    {
        SourceHandledOwnersThisCommand.Clear();
        SourceSupportedResizeOwnersThisCommand.Clear();
    }

    public static bool ShouldSuppressIncidentalChildManualStretch(
        ObjectId ownerId,
        string? globalCommandName)
    {
        if (!SourceSupportedResizeOwnersThisCommand.Contains(ownerId))
        {
            return false;
        }

        return RoofGeneratedMemberEditCommandRules.IsClassicStretch(globalCommandName) ||
               RoofGeneratedMemberEditCommandRules.IsGripStretchCommand(globalCommandName);
    }

    private static void MarkSourceSupportedResizeOwner(ObjectId ownerId) =>
        SourceSupportedResizeOwnersThisCommand.Add(ownerId);

    public static IReadOnlyCollection<ObjectId> Process(
        Document document,
        string? globalCommandName,
        IReadOnlyList<ObjectId> modifiedIds,
        IReadOnlyList<string> erasedSourceHandles,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName))
        {
#if DEBUG
            RoofUndoGuardDiag.Write(
                document.Editor,
                globalCommandName,
                "RoofLiveResizeService.Process");
#endif
            return Array.Empty<ObjectId>();
        }

        if (modifiedIds.Count == 0 &&
            erasedSourceHandles.Count == 0 &&
            !RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
        {
            return Array.Empty<ObjectId>();
        }

        try
        {
            var plan = Inspect(document.Database, modifiedIds, erasedSourceHandles, globalCommandName);
            if (plan.RelatedIds.Count == 0)
            {
                return Array.Empty<ObjectId>();
            }

            // Locked source ERASE has first ownership priority after Undo/Redo suppression.
            // Restore the authoritative source before evaluating any remaining source edits.
            if (plan.SourceEraseOwnerIds.Count > 0)
            {
                _ = ApplySourceEraseTampers(
                    document,
                    plan.SourceEraseOwnerIds,
                    erasedSourceHandles,
                    globalCommandName);
            }

            if (plan.ResizeOwnerIds.Count > 0)
            {
                foreach (var ownerId in plan.ResizeOwnerIds)
                {
                    SourceHandledOwnersThisCommand.Add(ownerId);
                }

                ApplyResizes(document, plan.ResizeOwnerIds, globalCommandName);
            }

            if (plan.UnsupportedOwnerIds.Count > 0)
            {
                foreach (var ownerId in plan.UnsupportedOwnerIds)
                {
                    SourceHandledOwnersThisCommand.Add(ownerId);
                }

                var recoverableUnsupportedOwnerIds = plan.UnsupportedOwnerIds;
                if (recoverableUnsupportedOwnerIds.Count > 0 &&
                    LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
                {
                    var recovery = TryRecoverUnsupportedOwners(
                        document,
                        globalCommandName,
                        recoverableUnsupportedOwnerIds);
                    if (recovery == UnsupportedRecoveryBatchResult.RecoveredAll)
                    {
                        TransientNotificationService.Show(
                            "Command_Roof_UnsupportedStretchRecoveredNotificationTitle",
                            "Command_Roof_UnsupportedStretchRecoveredNotificationBody");
                    }
                    else
                    {
                        // Safe fallback when snapshot missing/ambiguous/hard-fail.
                        document.Editor.WriteMessage(
                            UiStrings.GetString("Command_Roof_PersistedStale"));
                        TransientNotificationService.Show(
                            "Command_Roof_UnsupportedStretchNotificationTitle",
                            "Command_Roof_UnsupportedStretchNotificationBody");
                    }
                }
                else if (recoverableUnsupportedOwnerIds.Count > 0)
                {
                    document.Editor.WriteMessage(
                        UiStrings.GetString("Command_Roof_PersistedStale"));
                }
            }

            // Derived display edits are never geometry authority. Inspect applies the
            // edit-state-aware command policy; source resize/unsupported outcomes retain
            // precedence, including deferred rebuild events from the same command.
            IReadOnlyCollection<ObjectId> displayTamperOwners = plan.DisplayTamperOwnerIds;
            if (displayTamperOwners.Count > 0 &&
                LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName))
            {
                // Coherent rigid GROUP transform (true MOVE-like grip) before side-resize
                // adoption or DisplayTamper repair.
                displayTamperOwners = TryAcceptRigidGroupTransforms(
                    document,
                    displayTamperOwners,
                    modifiedIds,
                    globalCommandName);
            }

            if (displayTamperOwners.Count > 0 &&
                LiveGeometryCommandRules.IsGripStretchCommand(globalCommandName))
            {
                displayTamperOwners = TryAdoptGroupGripResizes(
                    document,
                    displayTamperOwners,
                    globalCommandName);
            }

            if (displayTamperOwners.Count > 0 &&
                ApplyDisplayTampers(document, displayTamperOwners, modifiedIds, erasedSourceHandles, globalCommandName))
            {
                TransientNotificationService.Show(
                    "Command_Roof_DisplayTamperNotificationTitle",
                    "Command_Roof_DisplayTamperNotificationBody");
            }

            // Locked native ERASE of Generated children restores the exact DBObjects.
            // Timber is un-erased before its SourceHandle-owned annotations; one final
            // owner-scoped GROUP sync canonicalizes mixed and multi-entity selection.
            if (plan.GeneratedTimberEraseOwnerIds.Count > 0 ||
                plan.GeneratedAnnotationEraseOwnerIds.Count > 0)
            {
                _ = ApplyGeneratedChildEraseTampers(
                    document,
                    plan.GeneratedTimberEraseOwnerIds,
                    plan.GeneratedAnnotationEraseOwnerIds,
                    erasedSourceHandles,
                    globalCommandName);
            }

            // Existing fallback behavior, including Unlocked ERASE suppression and
            // locked non-ERASE tamper recovery, runs only after exact H9 recovery.
            if (plan.GeneratedMemberTamperOwnerIds.Count > 0 &&
                RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(globalCommandName))
            {
                foreach (var ownerId in plan.GeneratedMemberTamperOwnerIds)
                {
                    SourceHandledOwnersThisCommand.Add(ownerId);
                }

                _ = RoofGeneratedMemberManualEditService.ProcessOwners(
                    document,
                    globalCommandName,
                    plan.GeneratedMemberTamperOwnerIds,
                    modifiedIds,
                    appendedTimberIds);
            }

            RelocateUnlockIndicators(document, globalCommandName, plan);
            return plan.RelatedIds;
        }
        catch (System.Exception ex)
        {
            document.Editor.WriteMessage(
                UiStrings.Format(UiStrings.WarningLiveRefreshSkippedFormat, ex.Message));
            return Array.Empty<ObjectId>();
        }
    }

    public static bool TryBeginGroupedUndo(Document document) =>
        TryInvokeUndoMark(document, "StartUndoMark");

    public static void TryEndGroupedUndo(Document document, bool markOpen)
    {
        if (markOpen)
        {
            _ = TryInvokeUndoMark(document, "EndUndoMark");
        }
    }

    private static void RelocateUnlockIndicators(
        Document document,
        string? globalCommandName,
        InspectionPlan plan)
    {
        var normalized = LiveGeometryCommandRules.NormalizeCommandName(globalCommandName);
        if (!normalized.Equals("MOVE", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("ROTATE", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var ownerIds = new HashSet<ObjectId>(plan.RelatedIds);
        // Resize owners already synced their unlock indicator inside the same
        // ApplyResizes transaction. Re-syncing them here would open a SECOND write
        // transaction for the same logical roof operation and split the native
        // undo/redo unit. Skip them so one SupportedResize stays one transaction.
        ownerIds.ExceptWith(plan.ResizeOwnerIds);
        if (ownerIds.Count == 0)
        {
            return;
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
#if DEBUG
            RoofRedoStateDiag.TraceTxn("relocate-unlock-indicators", "begin");
#endif
            var wrote = false;
            foreach (var ownerId in ownerIds)
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                        transaction,
                        ownerId,
                        OpenMode.ForRead,
                        out var owner,
                        document.Database) ||
                    owner is null ||
                    RoofDefinitionStore.Read(owner).Data is null)
                {
                    continue;
                }

                owner.UpgradeOpen();
                RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }
        }
    }

    private static InspectionPlan Inspect(
        Database database,
        IReadOnlyList<ObjectId> modifiedIds,
        IReadOnlyList<string> erasedSourceHandles,
        string? globalCommandName)
    {
        var related = new HashSet<ObjectId>();
        var resizeOwners = new HashSet<ObjectId>();
        var unsupportedOwners = new HashSet<ObjectId>();
        var displayTamperCandidates = new HashSet<ObjectId>();
        var generatedMemberTamperCandidates = new HashSet<ObjectId>();
        using var transaction = database.TransactionManager.StartTransaction();

        var sourceEraseOwners = new HashSet<ObjectId>();
        var generatedTimberEraseOwners = new HashSet<ObjectId>();
        var generatedAnnotationEraseOwners = new HashSet<ObjectId>();
        if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
        {
            var erasedSet = new HashSet<string>(erasedSourceHandles, StringComparer.OrdinalIgnoreCase);
            foreach (var ownerId in RoofDisplayErasePreCommandMapService.CollectLockedSourceEraseOwners(
                         erasedSet,
                         globalCommandName))
            {
                related.Add(ownerId);
                sourceEraseOwners.Add(ownerId);
            }

            foreach (var ownerId in RoofDisplayErasePreCommandMapService
                         .CollectLockedGeneratedEraseOwners(
                             erasedSet,
                             RoofEraseMappedKind.GeneratedTimber,
                             globalCommandName))
            {
                related.Add(ownerId);
                generatedTimberEraseOwners.Add(ownerId);
            }

            foreach (var ownerId in RoofDisplayErasePreCommandMapService
                         .CollectLockedGeneratedEraseOwners(
                             erasedSet,
                             RoofEraseMappedKind.GeneratedAnnotation,
                             globalCommandName))
            {
                related.Add(ownerId);
                generatedAnnotationEraseOwners.Add(ownerId);
            }

            // Prefer the read-only pre-command display→owner map. Do not depend on
            // post-erase XData or on assembly DisplayHandles that historically required
            // generated timber to be present.
            foreach (var ownerId in RoofDisplayErasePreCommandMapService.CollectDisplayOwners(erasedSet))
            {
                // Locked source recovery owns source+display ERASE in the same command.
                if (sourceEraseOwners.Contains(ownerId) ||
                    RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(ownerId, erasedSet))
                {
                    continue;
                }

                related.Add(ownerId);
                displayTamperCandidates.Add(ownerId);
            }

            foreach (var ownerId in RoofUnsupportedStretchRecoverySnapshotService.GetOwnerIds())
            {
                if (sourceEraseOwners.Contains(ownerId) ||
                    RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(ownerId, erasedSet))
                {
                    continue;
                }

                if (RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry) &&
                    entry.Assembly.DisplayHandles is not null &&
                    entry.Assembly.DisplayHandles.Any(handle => erasedSet.Contains(handle)))
                {
                    related.Add(ownerId);
                    displayTamperCandidates.Add(ownerId);
                }
            }
        }

        foreach (var id in modifiedIds.Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            if (RoofUnlockIndicatorStore.Exists(entity))
            {
                continue;
            }

            if (RoofDisplayStore.Read(entity).Exists)
            {
                related.Add(id);
                var resolution = RoofOwnerSelectionResolver.Resolve(database, transaction, id);
                if (resolution.IsResolved)
                {
                    related.Add(resolution.OwnerId);
                    displayTamperCandidates.Add(resolution.OwnerId);
                }
            }

            if (TryResolveGeneratedAssemblyOwner(
                    database,
                    transaction,
                    entity,
                    out var generatedOwnerId))
            {
                related.Add(id);
                related.Add(generatedOwnerId);
                generatedMemberTamperCandidates.Add(generatedOwnerId);
            }

            if (entity is not Polyline polyline)
            {
                continue;
            }

            var storedDefinition = RoofDefinitionStore.Read(polyline).Data;
            if (storedDefinition is null)
            {
                continue;
            }

            related.Add(id);
            switch (ClassifyOwner(
                        database,
                        transaction,
                        polyline,
                        treatHipDisplayDriftAsResize: true).Kind)
            {
                case RoofSourceChangeKind.SupportedResize:
                    resizeOwners.Add(id);
                    break;
                case RoofSourceChangeKind.Unsupported:
                    unsupportedOwners.Add(id);
                    break;
            }
        }

        var displayTamperOwners = new HashSet<ObjectId>();
        foreach (var ownerId in displayTamperCandidates)
        {
            // Source lifecycle wins: one outcome per owner per command, including
            // deferred display-rebuild batches after SupportedResize/Unsupported.
            if (resizeOwners.Contains(ownerId) ||
                unsupportedOwners.Contains(ownerId) ||
                SourceHandledOwnersThisCommand.Contains(ownerId))
            {
                continue;
            }

            if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName) &&
                (sourceEraseOwners.Contains(ownerId) ||
                 RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(
                     ownerId,
                     erasedSourceHandles)))
            {
                // Source ERASE: Locked recovery owns full roof restore; Unlocked
                // intentional source deletion must not resurrect orphaned display.
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var displayOwner,
                    database) ||
                displayOwner is null)
            {
                continue;
            }

            var displayOwnerDefinition = RoofDefinitionStore.Read(displayOwner).Data;
            if (displayOwnerDefinition is null ||
                !RoofDisplayTamperRepairRules.ShouldRepair(
                    displayOwnerDefinition.EditState,
                    globalCommandName))
            {
                continue;
            }

            if (RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName) &&
                !RoofDisplayErasePreCommandMapRules.ShouldClassifyLockedDisplayEraseTamper(
                    RoofEraseMappedKind.Display,
                    sourceErasedInSameCommand: false,
                    displayOwnerDefinition.EditState,
                    globalCommandName))
            {
                continue;
            }

            displayTamperOwners.Add(ownerId);
        }

        var generatedMemberTamperOwners = new HashSet<ObjectId>();
        foreach (var ownerId in generatedMemberTamperCandidates)
        {
            if (resizeOwners.Contains(ownerId) ||
                unsupportedOwners.Contains(ownerId) ||
                SourceHandledOwnersThisCommand.Contains(ownerId))
            {
                continue;
            }

            // Only when the source footprint itself is still valid. Child-only edits must
            // not require RigidEquivalent alone: a false SupportedResize from a weak Hip
            // compact descriptor must not suppress Locked generated recovery. When the
            // authoritative source polyline was also modified, source resize/unsupported
            // owns the owner and this loop already skipped above.
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    database) ||
                owner is null)
            {
                continue;
            }

            var ownerDefinition = RoofDefinitionStore.Read(owner).Data;
            if (ownerDefinition is null)
            {
                continue;
            }

            var sourceModified = modifiedIds.Contains(ownerId);
            ClassifyModifiedGeneratedChildren(
                database,
                transaction,
                owner,
                modifiedIds,
                out var generatedTimberModified,
                out var ownedAnnotationModified);

            // Unlocked annotation-only MOVE/ROTATE stays on the existing
            // presentation-only path (PersistFramedManualOffsets). Do not force
            // TryAcceptUnlockedEdits / snapshot recovery for label presentation.
            if (RoofGeneratedMemberLockedTamperRules.ShouldDeferUnlockedAnnotationPresentationOnly(
                    ownerDefinition.EditState,
                    generatedTimberModified,
                    ownedAnnotationModified))
            {
                continue;
            }

            if (!generatedTimberModified && !ownedAnnotationModified && !sourceModified)
            {
                continue;
            }

            var classification = ClassifyOwner(database, transaction, owner);
            if (classification.Geometry is null ||
                classification.Kind == RoofSourceChangeKind.Unsupported ||
                classification.Kind == RoofSourceChangeKind.None)
            {
                continue;
            }

            if (sourceModified &&
                classification.Kind != RoofSourceChangeKind.RigidEquivalent)
            {
                continue;
            }

            if (!sourceModified &&
                classification.Kind is not (
                    RoofSourceChangeKind.RigidEquivalent or
                    RoofSourceChangeKind.SupportedResize))
            {
                continue;
            }

            generatedMemberTamperOwners.Add(ownerId);
        }

        if (LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
                .Equals("ERASE", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ownerId in RoofUnsupportedStretchRecoverySnapshotService.GetOwnerIds())
            {
                if (resizeOwners.Contains(ownerId) ||
                    unsupportedOwners.Contains(ownerId) ||
                    generatedMemberTamperOwners.Contains(ownerId) ||
                    generatedTimberEraseOwners.Contains(ownerId) ||
                    generatedAnnotationEraseOwners.Contains(ownerId))
                {
                    continue;
                }

                if (RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry) &&
                    (HasErasedGeneratedTimber(database, transaction, entry) ||
                     HasErasedOwnedGeneratedAnnotation(database, entry)))
                {
                    generatedMemberTamperOwners.Add(ownerId);
                    related.Add(ownerId);
                }
            }
        }

        return new InspectionPlan(
            related,
            resizeOwners,
            unsupportedOwners,
            displayTamperOwners,
            generatedMemberTamperOwners,
            sourceEraseOwners,
            generatedTimberEraseOwners,
            generatedAnnotationEraseOwners);
    }

    private static bool HasErasedDerivedDisplay(
        Database database,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry entry)
    {
        foreach (var handle in entry.Assembly.DisplayHandles ?? Array.Empty<string>())
        {
            try
            {
                if (!long.TryParse(
                        handle,
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var handleValue))
                {
                    continue;
                }

                var id = database.GetObjectId(false, new Handle(handleValue), 0);
                if (!id.IsNull && id.IsErased)
                {
                    return true;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }

        return false;
    }

    private static bool HasErasedGeneratedTimber(
        Database database,
        Transaction transaction,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry entry)
    {
        _ = transaction;
        foreach (var timber in entry.Assembly.TimberLines)
        {
            try
            {
                if (!long.TryParse(
                        timber.EntityHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var handleValue))
                {
                    continue;
                }

                var id = database.GetObjectId(false, new Handle(handleValue), 0);
                if (!id.IsNull && id.IsErased)
                {
                    return true;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }

        return false;
    }

    private static bool HasErasedOwnedGeneratedAnnotation(
        Database database,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry entry)
    {
        foreach (var annotation in entry.Assembly.Annotations)
        {
            try
            {
                if (!long.TryParse(
                        annotation.EntityHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var handleValue))
                {
                    continue;
                }

                var id = database.GetObjectId(false, new Handle(handleValue), 0);
                if (!id.IsNull && id.IsErased)
                {
                    return true;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }

        return false;
    }

    private static void ClassifyModifiedGeneratedChildren(
        Database database,
        Transaction transaction,
        Polyline owner,
        IReadOnlyList<ObjectId> modifiedIds,
        out bool generatedTimberModified,
        out bool ownedAnnotationModified)
    {
        generatedTimberModified = false;
        ownedAnnotationModified = false;
        var ownerHandle = owner.Handle.ToString();
        var generatedIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            ownerHandle);
        var generatedIdSet = new HashSet<ObjectId>(generatedIds);
        var timberSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var timberId in generatedIds)
        {
            if (modifiedIds.Contains(timberId))
            {
                generatedTimberModified = true;
            }

            if (AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    timberId,
                    OpenMode.ForRead,
                    out var timber,
                    database) &&
                timber is not null)
            {
                timberSourceHandles.Add(timber.Handle.ToString());
            }
        }

        foreach (var id in modifiedIds.Distinct())
        {
            if (id.IsNull || id.IsErased || generatedIdSet.Contains(id))
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            if (!TryResolveAnnotationSourceHandle(entity, out var sourceHandle) ||
                string.IsNullOrWhiteSpace(sourceHandle) ||
                !timberSourceHandles.Contains(sourceHandle))
            {
                continue;
            }

            ownedAnnotationModified = true;
            if (generatedTimberModified)
            {
                return;
            }
        }
    }

    private static bool TryResolveGeneratedAssemblyOwner(
        Database database,
        Transaction transaction,
        Entity entity,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        var attached = RoofAttachedManualTimberStore.Read(entity);
        if (attached.Data is not null &&
            !string.IsNullOrWhiteSpace(attached.Data.RoofOwnerReference) &&
            TryResolveHandleToOwnerPolyline(
                database,
                transaction,
                attached.Data.RoofOwnerReference,
                out ownerId))
        {
            return true;
        }

        var timber = RoofGeneratedTimberStore.Read(entity);
        if (timber.Data is not null &&
            !string.IsNullOrWhiteSpace(timber.Data.RoofOwnerReference) &&
            TryResolveHandleToOwnerPolyline(
                database,
                transaction,
                timber.Data.RoofOwnerReference,
                out ownerId))
        {
            return true;
        }

        var hasAnnotationMetadata = TryResolveAnnotationSourceHandle(entity, out var sourceHandle);
        if (!hasAnnotationMetadata || string.IsNullOrWhiteSpace(sourceHandle))
        {
#if DEBUG
            if (LooksLikeAnnotationCandidate(entity))
            {
                RoofAnnotationOwnerResolutionDiag.Write(
                    entity.Handle.ToString(),
                    entity.GetType().Name,
                    sourceHandle: "-",
                    owner: "-",
                    result: "miss",
                    reason: "annotation-metadata-missing");
            }
#endif
            return false;
        }

        if (!TryResolveHandleToEntity(database, transaction, sourceHandle, out var sourceEntity) ||
            sourceEntity is null)
        {
#if DEBUG
            RoofAnnotationOwnerResolutionDiag.Write(
                entity.Handle.ToString(),
                entity.GetType().Name,
                sourceHandle,
                owner: "-",
                result: "miss",
                reason: "source-handle-unresolved");
#endif
            return false;
        }

        var sourceAttached = RoofAttachedManualTimberStore.Read(sourceEntity);
        if (sourceAttached.Data is not null &&
               !string.IsNullOrWhiteSpace(sourceAttached.Data.RoofOwnerReference) &&
               TryResolveHandleToOwnerPolyline(
                   database,
                   transaction,
                   sourceAttached.Data.RoofOwnerReference,
                   out ownerId))
        {
            return true;
        }

        var sourceTimber = RoofGeneratedTimberStore.Read(sourceEntity);
        if (sourceTimber.Data is not null &&
               !string.IsNullOrWhiteSpace(sourceTimber.Data.RoofOwnerReference) &&
               TryResolveHandleToOwnerPolyline(
                   database,
                   transaction,
                   sourceTimber.Data.RoofOwnerReference,
                   out ownerId))
        {
            return true;
        }

#if DEBUG
        RoofAnnotationOwnerResolutionDiag.Write(
            entity.Handle.ToString(),
            entity.GetType().Name,
            sourceHandle,
            owner: "-",
            result: "miss",
            reason: "timber-owner-unresolved");
#endif
        return false;
    }

    private static bool LooksLikeAnnotationCandidate(Entity entity) =>
        entity is MLeader or DBText or MText ||
        ElementLabelStore.TryRead(entity, out _) ||
        SlopeArrowStore.TryRead(entity, out _) ||
        SlopeAngleTextStore.TryRead(entity, out _) ||
        PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _);

    private static bool TryResolveAnnotationSourceHandle(Entity entity, out string sourceHandle)
    {
        sourceHandle = string.Empty;
        if (ElementLabelStore.TryRead(entity, out var label) &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.SourceHandle))
        {
            sourceHandle = label.SourceHandle;
            return true;
        }

        if (SlopeArrowStore.TryRead(entity, out var arrow) &&
            arrow is not null &&
            !string.IsNullOrWhiteSpace(arrow.SourceHandle))
        {
            sourceHandle = arrow.SourceHandle;
            return true;
        }

        if (SlopeAngleTextStore.TryRead(entity, out var angle) &&
            angle is not null &&
            !string.IsNullOrWhiteSpace(angle.SourceHandle))
        {
            sourceHandle = angle.SourceHandle;
            return true;
        }

        if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
            post is not null &&
            !string.IsNullOrWhiteSpace(post.SourceHandle))
        {
            sourceHandle = post.SourceHandle;
            return true;
        }

        return false;
    }

    private static bool TryResolveHandleToOwnerPolyline(
        Database database,
        Transaction transaction,
        string handleText,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        return TryResolveHandleToEntity(database, transaction, handleText, out var entity) &&
               entity is Polyline &&
               RoofDefinitionStore.Read(entity).Data is not null &&
               (ownerId = entity.ObjectId) != ObjectId.Null;
    }

    private static bool TryResolveHandleToEntity(
        Database database,
        Transaction transaction,
        string handleText,
        out Entity? entity)
    {
        entity = null;
        if (!long.TryParse(
                handleText,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        try
        {
            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            return !id.IsNull &&
                   !id.IsErased &&
                   AutoCadObjectIdAccess.TryGetObject(
                       transaction,
                       id,
                       OpenMode.ForRead,
                       out entity,
                       database) &&
                   entity is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static UnsupportedRecoveryBatchResult TryRecoverUnsupportedOwners(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> ownerIds)
    {
        if (!RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand(globalCommandName) ||
            ownerIds.Count == 0)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch",
                reason: ownerIds.Count == 0 ? "no-unsupported-owners" : "command-not-recovery-eligible",
                kind: globalCommandName);
#endif
            return UnsupportedRecoveryBatchResult.Unavailable;
        }

        // All-or-nothing eligibility: never partially restore one owner while another
        // unsupported owner in the same command lacks a snapshot.
        using (document.LockDocument())
        using (var probe = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var ownerId in ownerIds)
            {
                if (!TryProbeUnsupportedOwner(
                        document,
                        probe,
                        globalCommandName,
                        ownerId,
                        ownerIds.Count))
                {
                    return UnsupportedRecoveryBatchResult.Unavailable;
                }
            }
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            try
            {
                foreach (var ownerId in ownerIds)
                {
                    var outcome = RoofUnsupportedStretchRecoveryService.TryRecoverOwner(
                        document.Database,
                        transaction,
                        ownerId,
                        document.Editor);
                    if (outcome != RoofUnsupportedStretchRecoveryOutcome.Recovered)
                    {
                        // Abort: leave native Unsupported geometry; caller shows fallback.
#if DEBUG
                        if (ownerIds.Count > 1)
                        {
                            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                                document.Editor,
                                stage: "batch-restore",
                                reason: "multi-owner-all-or-nothing-rejection",
                                owner: ownerId.Handle.ToString(),
                                kind: outcome.ToString());
                        }
#endif
                        return outcome == RoofUnsupportedStretchRecoveryOutcome.HardFailure
                            ? UnsupportedRecoveryBatchResult.HardFailure
                            : UnsupportedRecoveryBatchResult.Unavailable;
                    }
                }

                transaction.Commit();
                return UnsupportedRecoveryBatchResult.RecoveredAll;
            }
#if DEBUG
            catch (System.Exception ex)
            {
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    document.Editor,
                    stage: "batch-restore",
                    reason: "transaction-abort-exception",
                    detail: ex.GetType().Name);
                return UnsupportedRecoveryBatchResult.HardFailure;
            }
#else
            catch (System.Exception)
            {
                return UnsupportedRecoveryBatchResult.HardFailure;
            }
#endif

        }
    }

    private static UnsupportedRecoveryBatchResult TryRecoverGeneratedMemberOwners(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> ownerIds)
    {
        if (!RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand(globalCommandName) ||
            ownerIds.Count == 0)
        {
            return UnsupportedRecoveryBatchResult.Unavailable;
        }

        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            try
            {
                foreach (var ownerId in ownerIds)
                {
                    var outcome = RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(
                        document.Database,
                        transaction,
                        ownerId,
                        document.Editor);
                    if (outcome != RoofUnsupportedStretchRecoveryOutcome.Recovered)
                    {
#if DEBUG
                        RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                            document.Editor,
                            stage: "generated-only",
                            reason: outcome.ToString(),
                            owner: ownerId.IsNull ? "-" : ownerId.Handle.ToString());
#endif
                        return outcome == RoofUnsupportedStretchRecoveryOutcome.HardFailure
                            ? UnsupportedRecoveryBatchResult.HardFailure
                            : UnsupportedRecoveryBatchResult.Unavailable;
                    }
                }

                transaction.Commit();
                return UnsupportedRecoveryBatchResult.RecoveredAll;
            }
#if DEBUG
            catch (System.Exception ex)
            {
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    document.Editor,
                    stage: "generated-only",
                    reason: "transaction-abort-exception",
                    detail: ex.GetType().Name);
                return UnsupportedRecoveryBatchResult.HardFailure;
            }
#else
            catch (System.Exception)
            {
                return UnsupportedRecoveryBatchResult.HardFailure;
            }
#endif
        }
    }

    private static bool TryProbeUnsupportedOwner(
        Document document,
        Transaction probe,
        string? globalCommandName,
        ObjectId ownerId,
        int ownerCount)
    {
        _ = ownerCount;
#if DEBUG
        var snapshotCount = RoofUnsupportedStretchRecoverySnapshotService.SnapshotCount;
        var clearReason = RoofUnsupportedStretchRecoverySnapshotService.LastClearReason;
        var clearCommand = RoofUnsupportedStretchRecoverySnapshotService.LastClearCommand;
        var captureCommand = RoofUnsupportedStretchRecoverySnapshotService.LastCaptureCommand;
#endif

        if (ownerId.IsNull)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "roof-source-objectid-missing");
#endif
            return false;
        }

        if (ownerId.IsErased)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "roof-source-erased",
                owner: ownerId.Handle.ToString());
#endif
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                probe,
                ownerId,
                OpenMode.ForRead,
                out var entity,
                document.Database) ||
            entity is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "roof-source-missing",
                owner: ownerId.Handle.ToString());
#endif
            return false;
        }

        if (entity is not Polyline owner)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "roof-source-type-mismatch",
                owner: entity.Handle.ToString(),
                kind: entity.GetType().Name);
#endif
            return false;
        }

        var liveHandle = owner.Handle.ToString();
        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var snap))
        {
#if DEBUG
            var skips = RoofUnsupportedStretchRecoverySnapshotService.GetCaptureSkips();
            var skipDetail = skips.Count > 0 ? string.Join("|", skips) : null;
            string reason;
            if (snapshotCount == 0 &&
                string.Equals(clearReason, "non-recovery-command", StringComparison.Ordinal))
            {
                reason = "no-command-snapshot";
            }
            else if (snapshotCount == 0 &&
                     string.Equals(clearReason, "CommandEnded", StringComparison.Ordinal))
            {
                reason = "snapshot-cleared-too-early";
            }
            else if (snapshotCount == 0)
            {
                reason = "no-command-snapshot";
            }
            else
            {
                reason = "owner-snapshot-missing";
            }

            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: reason,
                owner: liveHandle,
                kind: $"clear={clearReason ?? "-"}/clearCmd={clearCommand ?? "-"}/captureCmd={captureCommand ?? "-"}/count={snapshotCount}",
                detail: skipDetail);
            if (ownerCount > 1)
            {
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    document.Editor,
                    stage: "batch-probe",
                    reason: "multi-owner-all-or-nothing-rejection",
                    owner: liveHandle);
            }
#endif
            return false;
        }

        if (!string.Equals(
                snap.Assembly.RoofSource.OwnerHandle,
                liveHandle,
                StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "ambiguous-owner-match",
                owner: liveHandle,
                handle: snap.Assembly.RoofSource.OwnerHandle);
#endif
            return false;
        }

        if (!RoofUnsupportedStretchRecoveryRules.CanAttemptAssemblyRecovery(
                globalCommandName,
                snap.Assembly,
                liveHandle,
                RoofSourceChangeKind.Unsupported))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                document.Editor,
                stage: "batch-probe",
                reason: "probe-validation-failure",
                owner: liveHandle,
                kind: globalCommandName);
            RoofUnsupportedStretchRecoveryDiag.WriteProbe(
                document.Editor,
                liveHandle,
                roof: 1,
                timber: snap.Assembly.TimberLines.Count,
                annotations: snap.Assembly.Annotations.Count,
                result: "reject",
                kindCounts: RoofUnsupportedStretchRecoverySnapshotService.FormatAnnotationKindCounts(
                    snap.Assembly));
#endif
            return false;
        }

#if DEBUG
        RoofUnsupportedStretchRecoveryDiag.WriteProbe(
            document.Editor,
            liveHandle,
            roof: 1,
            timber: snap.Assembly.TimberLines.Count,
            annotations: snap.Assembly.Annotations.Count,
            result: "ok",
            kindCounts: RoofUnsupportedStretchRecoverySnapshotService.FormatAnnotationKindCounts(
                snap.Assembly));
#endif
        return true;
    }

    private static void ApplyResizes(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds,
        string? globalCommandName)
    {
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
#if DEBUG
            RoofRedoStateDiag.TraceTxn("apply-resizes", "begin");
#endif
            var wrote = false;
            try
            {
                foreach (var ownerId in ownerIds)
                {
                    var result = TryApplyResize(document, transaction, ownerId, globalCommandName);
                    if (result == ResizeApplyResult.HardFailure)
                    {
                        document.Editor.WriteMessage(
                            UiStrings.GetString("Command_RoofRafters_GenerationFailed"));
                        return;
                    }

                    if (result == ResizeApplyResult.Applied)
                    {
                        wrote = true;
                    }
                }

                if (wrote)
                {
                    transaction.Commit();
#if DEBUG
                    RoofRedoStateDiag.TraceTxn("apply-resizes", "commit");
#endif
                }
            }
            catch (System.Exception)
            {
                document.Editor.WriteMessage(
                    UiStrings.GetString("Command_RoofRafters_GenerationFailed"));
            }
        }
    }

    private static ResizeApplyResult TryApplyResize(
        Document document,
        Transaction transaction,
        ObjectId ownerId,
        string? globalCommandName)
    {
        _ = globalCommandName;
        var database = document.Database;
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var classification = ClassifyOwner(
            database,
            transaction,
            owner,
            treatHipDisplayDriftAsResize: true);
        if (classification.Kind != RoofSourceChangeKind.SupportedResize ||
            classification.Geometry is null)
        {
            return ResizeApplyResult.Skipped;
        }

        MarkSourceSupportedResizeOwner(ownerId);
#if DEBUG
        RoofRedoStateDiag.Capture(
            database,
            transaction,
            ownerId,
            owner.Handle.ToString(),
            "before-resize");
        RoofRedoStateDiag.CaptureOwnershipInvariant(
            database,
            transaction,
            ownerId,
            owner.Handle.ToString());
#endif
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var storedDefinition = RoofDefinitionStore.Read(owner).Data;
        if (storedDefinition is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var isHip = classification.Geometry is HipRoofGeometry;
        var updated = isHip
            ? RoofDefinitionPersistence.UpdateGeometry(
                storedDefinition,
                input,
                classification.Geometry)
            : RoofGeneratedMemberOverrideRules.PreserveEditState(
                RoofDefinitionPersistence.Create(
                    input,
                    validation.Footprint,
                    classification.Geometry),
                storedDefinition);
        RoofDefinitionStore.Write(owner, transaction, updated);
        var edges = RoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = RoofWireframe.BuildGenerationSignature(edges);
        if (!RoofDisplayService.Rebuild(
                database,
                transaction,
                owner.ObjectId,
                owner.Handle.ToString(),
                edges,
                signature))
        {
            return ResizeApplyResult.HardFailure;
        }

        var generatedMemberCount = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            owner.Handle.ToString()).Count;
        var rafterOutcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
            database,
            transaction,
            document.Editor,
            owner,
            classification.Geometry,
            TimberElementDefaultProfileStore.Load(),
            ElementLayerProfileStore.Load(),
            out var anchorResolutionContext,
            out var generatedReplayPlan,
            forceRegenerateOnSourceResize: true,
            rebuildReason: "source-resize");
        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.Failed)
        {
            return ResizeApplyResult.HardFailure;
        }

        // Hip ordinary rafters must rematerialize with the rebuilt display. Returning
        // Applied after a skipped replace leaves expanded faces visually uncovered.
        if (isHip &&
            generatedMemberCount > 0 &&
            rafterOutcome != RoofGeneratedRafterSetService.ReplacementOutcome.Replaced)
        {
            return ResizeApplyResult.HardFailure;
        }

        _ = RoofSourceResizeChildPolicyService.Apply(
            document,
            transaction,
            owner,
            rafterOutcome,
            generatedMemberCount,
            generatedReplayPlan?.GeometryReplayCount ?? 0,
            anchorResolutionContext,
            replayAttachedManualChildren: true);

        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedAmbiguousRecipe)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_RecipeAmbiguous"));
        }
        else if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedInvalidLayout)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_InvalidSpacing"));
        }

        RoofUnlockIndicatorService.Sync(database, transaction, owner);
#if DEBUG
        if (isHip)
        {
            RoofRedoStateDiag.TraceHipResize(
                document.Editor,
                owner.Handle.ToString(),
                globalCommandName,
                input.Vertices?.Count ?? 0,
                edges);
            if (classification.Geometry is HipRoofGeometry hipForDiag)
            {
                RoofRafterPermanentCreateDiag.WriteHipSourcePolygon(
                    document.Editor,
                    owner.Handle.ToString(),
                    input);
                var afterIds = RoofGeneratedTimberStore.FindByOwner(
                    database,
                    transaction,
                    owner.Handle.ToString());
                if (RoofGeneratedRafterSetService.TryRecoverRecipe(
                        database,
                        transaction,
                        afterIds,
                        out var recipeAfter))
                {
                    var liveSolve = RoofRafterLayoutSolver.Solve(
                        hipForDiag,
                        new RafterLayoutParameters(
                            recipeAfter.MaximumSpacingMm,
                            recipeAfter.WidthMm));
                    if (liveSolve.IsValid && liveSolve.Layout is not null)
                    {
                        RoofRafterPermanentCreateDiag.WriteSolveInput(
                            document.Editor,
                            owner.Handle.ToString(),
                            input,
                            hipForDiag,
                            recipeAfter.MaximumSpacingMm,
                            liveSolve.Layout);
                        RoofRafterPermanentCreateDiag.WriteFaceCoverageSummary(
                            document.Editor,
                            hipForDiag,
                            liveSolve.Layout);
                    }
                }
            }
        }
        RoofRedoStateDiag.Capture(
            database,
            transaction,
            owner.ObjectId,
            owner.Handle.ToString(),
            "after-resize");
#endif
        return ResizeApplyResult.Applied;
    }

    private static IReadOnlyCollection<ObjectId> TryAcceptRigidGroupTransforms(
        Document document,
        IReadOnlyCollection<ObjectId> displayTamperOwnerIds,
        IReadOnlyList<ObjectId> modifiedIds,
        string? globalCommandName)
    {
        _ = globalCommandName;
        var remaining = new HashSet<ObjectId>(displayTamperOwnerIds);
        var accepted = new List<ObjectId>();
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            // Read-only accept path — no Commit needed; avoid write side-effects.
            foreach (var ownerId in displayTamperOwnerIds)
            {
                if (!RoofGroupGripRigidTransformService.TryAcceptRigidGroupTransform(
                        document.Database,
                        transaction,
                        ownerId,
                        modifiedIds,
                        out var rejectionReason,
                        out var result))
                {
                    _ = rejectionReason;
                    continue;
                }

                remaining.Remove(ownerId);
                accepted.Add(ownerId);
                SourceHandledOwnersThisCommand.Add(ownerId);
                _ = result;
            }
        }

        _ = accepted;
        return remaining;
    }

    private static IReadOnlyCollection<ObjectId> TryAdoptGroupGripResizes(
        Document document,
        IReadOnlyCollection<ObjectId> displayTamperOwnerIds,
        string? globalCommandName)
    {
        _ = globalCommandName;
        var remaining = new HashSet<ObjectId>(displayTamperOwnerIds);
        var adopted = new List<ObjectId>();
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            foreach (var ownerId in displayTamperOwnerIds)
            {
                if (!RoofGroupGripResizeAdoptionService.TryAdoptSupportedGroupGripResize(
                        document.Database,
                        transaction,
                        ownerId,
                        out var rejectionReason))
                {
                    _ = rejectionReason;
                    continue;
                }

                var resizeResult = TryApplyResize(document, transaction, ownerId, globalCommandName);
                if (resizeResult != ResizeApplyResult.Applied)
                {
                    continue;
                }

                remaining.Remove(ownerId);
                adopted.Add(ownerId);
                SourceHandledOwnersThisCommand.Add(ownerId);
                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }
        }

        _ = adopted;
        return remaining;
    }

    private static bool ApplySourceEraseTampers(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds,
        IReadOnlyList<string> erasedSourceHandles,
        string? globalCommandName)
    {
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            var erasedSet = new HashSet<string>(erasedSourceHandles, StringComparer.OrdinalIgnoreCase);
            foreach (var ownerId in ownerIds)
            {
                if (!RoofDisplayErasePreCommandMapService.TryGetSourceState(ownerId, out var state) ||
                    !RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedSourceErase(
                        state.EditState,
                        globalCommandName))
                {
                    continue;
                }

                var displayErasedCount =
                    RoofDisplayErasePreCommandMapService.CountErasedDisplaysForOwner(
                        ownerId,
                        erasedSet);
#if DEBUG
                RoofGeneratedMemberManualEditDiag.WriteSourceEraseTamper(
                    document.Editor,
                    state.OwnerHandle,
                    globalCommandName,
                    ownerId.ToString(),
                    state.OwnerHandle,
                    state.EditState.ToString(),
                    sourceErased: true,
                    displayErasedCount,
                    "LockedSourceEraseTamper",
                    "restore-source");
#else
                _ = displayErasedCount;
#endif

                if (!TryUnEraseLockedSource(
                        document.Database,
                        transaction,
                        ownerId,
                        state,
                        out var sameObjectId,
                        out var sameHandle))
                {
#if DEBUG
                    RoofGeneratedMemberManualEditDiag.WriteSourceEraseRepair(
                        document.Editor,
                        state.OwnerHandle,
                        sourceRestored: false,
                        sameObjectId: false,
                        sameHandle: false,
                        displayRebuilt: false,
                        groupMembers: -1,
                        canonical: false,
                        result: "failed-unerase");
#else
                    _ = sameObjectId;
                    _ = sameHandle;
#endif
                    continue;
                }

                var displayRebuilt = TryApplyDisplayTamper(
                    document.Database,
                    transaction,
                    ownerId);
                SourceHandledOwnersThisCommand.Add(ownerId);

#if DEBUG
                var groupMembers = -1;
                var canonical = false;
                if (RoofDisplayGroupService.TryOpenCanonicalGroup(
                        document.Database,
                        transaction,
                        ownerId,
                        OpenMode.ForRead,
                        out var group) && group is not null)
                {
                    groupMembers = group.GetAllEntityIds().Length;
                    canonical = true;
                }

                RoofGeneratedMemberManualEditDiag.WriteSourceEraseRepair(
                    document.Editor,
                    state.OwnerHandle,
                    sourceRestored: true,
                    sameObjectId,
                    sameHandle,
                    displayRebuilt,
                    groupMembers,
                    canonical,
                    result: displayRebuilt ? "Recovered|ok" : "source-ok-display-failed");
#else
                _ = sameObjectId;
                _ = sameHandle;
                _ = displayRebuilt;
#endif
                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }

            return wrote;
        }
    }

    private static bool TryUnEraseLockedSource(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        RoofDisplayErasePreCommandMapService.SourcePreCommandState state,
        out bool sameObjectId,
        out bool sameHandle)
    {
        sameObjectId = false;
        sameHandle = false;
        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
        {
            return false;
        }

        if (owner.IsErased)
        {
            owner.Erase(false);
        }

        if (owner.IsErased)
        {
            return false;
        }

        sameObjectId = owner.ObjectId == ownerId;
        var liveHandle = owner.Handle.ToString();
        sameHandle = string.Equals(
            liveHandle,
            state.OwnerHandle,
            StringComparison.OrdinalIgnoreCase);
        if (!sameObjectId || !sameHandle)
        {
            return false;
        }

        var definition = RoofDefinitionStore.Read(owner).Data;
        return definition is not null &&
               definition.EditState == state.EditState &&
               definition.Kind == state.Kind;
    }

    private static bool ApplyGeneratedChildEraseTampers(
        Document document,
        IReadOnlyCollection<ObjectId> timberOwnerIds,
        IReadOnlyCollection<ObjectId> annotationOwnerIds,
        IReadOnlyCollection<string> erasedHandles,
        string? globalCommandName)
    {
        var wroteAny = false;
        var owners = new HashSet<ObjectId>(timberOwnerIds);
        owners.UnionWith(annotationOwnerIds);
        using (document.LockDocument())
        {
            foreach (var ownerId in owners)
            {
                using var transaction = document.Database.TransactionManager.StartTransaction();
                if (!RoofDisplayErasePreCommandMapService.TryGetSourceState(ownerId, out var state))
                {
                    continue;
                }

                var timberEntries = RoofDisplayErasePreCommandMapService.GetErasedEntriesForOwner(
                    ownerId,
                    erasedHandles,
                    RoofEraseMappedKind.GeneratedTimber);
                var annotationEntries = RoofDisplayErasePreCommandMapService.GetErasedEntriesForOwner(
                    ownerId,
                    erasedHandles,
                    RoofEraseMappedKind.GeneratedAnnotation);
                if (timberEntries.Count == 0 && annotationEntries.Count == 0)
                {
                    continue;
                }

#if DEBUG
                RoofGeneratedMemberManualEditDiag.WriteGeneratedEraseTamper(
                    document.Editor,
                    state.OwnerHandle,
                    globalCommandName,
                    timberEntries.Count,
                    annotationEntries.Count,
                    state.EditState.ToString(),
                    RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(ownerId, erasedHandles),
                    "LockedGeneratedEraseTamper",
                    "un-erase-exact");
                if (annotationEntries.Count > 0)
                {
                    RoofGeneratedMemberManualEditDiag.WriteGeneratedAnnotationEraseTamper(
                        document.Editor,
                        state.OwnerHandle,
                        globalCommandName,
                        annotationEntries.Count,
                        state.EditState.ToString(),
                        "LockedGeneratedAnnotationEraseTamper",
                        "un-erase-exact");
                }
#endif

                var restoredTimber = 0;
                var restoredAnnotations = 0;
                var timberSameObjectId = true;
                var timberSameHandle = true;
                var annotationSameObjectId = true;
                var annotationSameHandle = true;
                var identityPreserved = true;
                foreach (var entry in timberEntries)
                {
                    if (!TryUnEraseGeneratedTimber(
                            document.Database,
                            transaction,
                            entry,
                            out var entrySameObjectId,
                            out var entrySameHandle))
                    {
                        identityPreserved = false;
                        break;
                    }

                    restoredTimber++;
                    timberSameObjectId &= entrySameObjectId;
                    timberSameHandle &= entrySameHandle;
                }

                if (identityPreserved)
                {
                    foreach (var entry in annotationEntries)
                    {
                        if (!TryUnEraseGeneratedAnnotation(
                                document.Database,
                                transaction,
                                entry,
                                out var entrySameObjectId,
                                out var entrySameHandle))
                        {
                            identityPreserved = false;
                            break;
                        }

                        restoredAnnotations++;
                        annotationSameObjectId &= entrySameObjectId;
                        annotationSameHandle &= entrySameHandle;
                    }
                }

                var synced = identityPreserved &&
                             RoofAssemblyGroupSyncService.TrySyncForOwner(
                                 document,
                                 transaction,
                                 ownerId);
                var groupMembers = -1;
                var canonical = false;
                if (synced &&
                    RoofDisplayGroupService.TryOpenCanonicalGroup(
                        document.Database,
                        transaction,
                        ownerId,
                        OpenMode.ForRead,
                        out var group) &&
                    group is not null)
                {
                    var postMembers = group.GetAllEntityIds();
                    groupMembers = postMembers.Length;
                    canonical = RoofAssemblyGroupMembershipRules.CountDuplicates(postMembers) == 0;
                }

                if (identityPreserved && canonical)
                {
                    transaction.Commit();
                    wroteAny = true;
                }

#if DEBUG
                RoofGeneratedMemberManualEditDiag.WriteGeneratedEraseRepair(
                    document.Editor,
                    state.OwnerHandle,
                    restoredTimber,
                    timberSameObjectId,
                    timberSameHandle,
                    identityPreserved,
                    groupMembers,
                    canonical,
                    identityPreserved && canonical ? "Recovered|ok" : "failed-exact-unerase");
                if (annotationEntries.Count > 0)
                {
                    RoofGeneratedMemberManualEditDiag.WriteGeneratedAnnotationEraseRepair(
                        document.Editor,
                        state.OwnerHandle,
                        annotationEntries.Count,
                        restoredAnnotations,
                        annotationSameObjectId,
                        annotationSameHandle,
                        identityPreserved && canonical ? "Recovered|ok" : "failed-exact-unerase");
                }
#endif
            }
        }

        return wroteAny;
    }

    private static bool TryUnEraseGeneratedTimber(
        Database database,
        Transaction transaction,
        RoofDisplayErasePreCommandMapService.MappedEntity entry,
        out bool sameObjectId,
        out bool sameHandle)
    {
        sameObjectId = false;
        sameHandle = false;
        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                transaction,
                entry.EntityId,
                OpenMode.ForWrite,
                out var entity,
                database) ||
            entity is null)
        {
            return false;
        }

        if (entity.IsErased)
        {
            entity.Erase(false);
        }

        sameObjectId = entity.ObjectId == entry.EntityId;
        sameHandle = string.Equals(
            entity.Handle.ToString(),
            entry.EntityHandle,
            StringComparison.OrdinalIgnoreCase);
        if (entity.IsErased || !sameObjectId || !sameHandle)
        {
            return false;
        }

        var generated = RoofGeneratedTimberStore.Read(entity).Data;
        var hasTimberData = ElementDataStore.TryRead(entity, transaction, out var timberData);
        return generated == entry.GeneratedData &&
               hasTimberData == (entry.TimberData is not null) &&
               timberData == entry.TimberData;
    }

    private static bool TryUnEraseGeneratedAnnotation(
        Database database,
        Transaction transaction,
        RoofDisplayErasePreCommandMapService.MappedEntity entry,
        out bool sameObjectId,
        out bool sameHandle)
    {
        sameObjectId = false;
        sameHandle = false;
        if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                transaction,
                entry.EntityId,
                OpenMode.ForWrite,
                out var entity,
                database) ||
            entity is null)
        {
            return false;
        }

        if (entity.IsErased)
        {
            entity.Erase(false);
        }

        sameObjectId = entity.ObjectId == entry.EntityId;
        sameHandle = string.Equals(
            entity.Handle.ToString(),
            entry.EntityHandle,
            StringComparison.OrdinalIgnoreCase);
        return !entity.IsErased &&
               sameObjectId &&
               sameHandle &&
               RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var sourceHandle) &&
               string.Equals(
                   sourceHandle,
                   entry.GeneratedSourceHandle,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApplyDisplayTampers(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds,
        IReadOnlyList<ObjectId> modifiedIds,
        IReadOnlyList<string> erasedSourceHandles,
        string? globalCommandName)
    {
        _ = modifiedIds;
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            var erasedSet = new HashSet<string>(erasedSourceHandles, StringComparer.OrdinalIgnoreCase);
            foreach (var ownerId in ownerIds)
            {
                if (SourceHandledOwnersThisCommand.Contains(ownerId))
                {
                    continue;
                }

                var sourceErased = RoofDisplayErasePreCommandMapService.IsOwnerSourceErased(
                    ownerId,
                    erasedSet);
                if (sourceErased)
                {
                    // Unlocked intentional source delete, or Locked source recovery already
                    // owns this owner — never rebuild display against a deleted source here.
                    continue;
                }

#if DEBUG
                if (AutoCadObjectIdAccess.TryGetObject<Polyline>(
                        transaction,
                        ownerId,
                        OpenMode.ForRead,
                        out var owner,
                        document.Database) && owner is not null)
                {
                    var definition = RoofDefinitionStore.Read(owner).Data;
                    var erasedCount =
                        RoofDisplayErasePreCommandMapService.CountErasedDisplaysForOwner(
                            ownerId,
                            erasedSet);
                    if (erasedCount == 0 &&
                        RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
                    {
                        erasedCount = entry.Assembly.DisplayHandles?.Count(h => erasedSet.Contains(h)) ?? 0;
                    }

                    RoofGeneratedMemberManualEditDiag.WriteDisplayEraseTamper(
                        document.Editor,
                        owner.Handle.ToString(),
                        globalCommandName,
                        erasedCount,
                        sourceErased: false,
                        sourceModified: modifiedIds.Contains(ownerId),
                        definition?.EditState.ToString(),
                        "LockedDisplayEraseTamper",
                        "repair-rebuild");
                }
#endif

                if (!TryApplyDisplayTamper(document.Database, transaction, ownerId))
                {
                    continue;
                }

#if DEBUG
                if (AutoCadObjectIdAccess.TryGetObject<Polyline>(
                        transaction,
                        ownerId,
                        OpenMode.ForRead,
                        out var repairedOwner,
                        document.Database) && repairedOwner is not null)
                {
                    if (RoofDisplayGroupService.TryOpenCanonicalGroup(
                            document.Database,
                            transaction,
                            ownerId,
                            OpenMode.ForRead,
                            out var group) && group is not null)
                    {
                        var memberCount = group.GetAllEntityIds().Length;
                        RoofGeneratedMemberManualEditDiag.WriteDisplayEraseRepair(
                            document.Editor,
                            repairedOwner.Handle.ToString(),
                            globalCommandName,
                            expectedDisplay: -1,
                            restoredDisplay: -1,
                            groupMembers: memberCount,
                            canonical: true,
                            result: "Recovered|ok");
                    }
                }
#endif
                wrote = true;
            }

            if (wrote)
            {
                transaction.Commit();
            }

            return wrote;
        }
    }

    private static bool TryApplyDisplayTamper(
        Database database,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
            return false;
        }

        // Source unchanged and still restores: rebuild disposable display cache only.
        var classification = ClassifyOwner(database, transaction, owner);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent ||
            classification.Geometry is null)
        {
            return false;
        }

        var edges = RoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = RoofWireframe.BuildGenerationSignature(edges);
        return RoofDisplayService.Rebuild(
            database,
            transaction,
            owner.ObjectId,
            owner.Handle.ToString(),
            edges,
            signature);
    }

    private static RoofSourceChangeClassification ClassifyOwner(
        Database database,
        Transaction transaction,
        Polyline polyline,
        bool treatHipDisplayDriftAsResize = false)
    {
        var stored = RoofDefinitionStore.Read(polyline);
        if (stored.Data is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.None,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        var input = RoofPolylineExtractor.Extract(polyline);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofSourceChangeClassification(
                RoofSourceChangeKind.Unsupported,
                null,
                RoofDefinitionRestoreError.StaleFootprint);
        }

        var geometric = RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
        if (treatHipDisplayDriftAsResize &&
            stored.Data.Kind == RoofKind.Hip &&
            geometric.Kind == RoofSourceChangeKind.RigidEquivalent &&
            geometric.Geometry is HipRoofGeometry hipGeometry)
        {
            var edges = RoofWireframe.Create(
                hipGeometry,
                RoofPolylineExtractor.GetSourceElevation(polyline));
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            var display = RoofDisplayService.Inspect(
                database,
                transaction,
                polyline.ObjectId,
                polyline.Handle.ToString(),
                edges,
                signature);
            if (!display.Validation.IsCurrent)
            {
                geometric = geometric with { Kind = RoofSourceChangeKind.SupportedResize };
            }
            else if (HipGeneratedRelativeCoverageMismatch(
                database,
                transaction,
                polyline,
                hipGeometry))
            {
                // Rectangle Hip can still keep Edge01/Edge12 while generated timber
                // no longer matches the solved layout (native STRETCH left old lattice).
                geometric = geometric with { Kind = RoofSourceChangeKind.SupportedResize };
            }
        }

        return geometric with
        {
            Kind = RoofSourceChangeEditStatePolicy.EffectiveKind(
                stored.Data.Kind,
                stored.Data.EditState,
                geometric.Kind),
        };
    }

    /// <summary>
    /// Translation-invariant mismatch between existing generated timber and the layout
    /// solved from the current Hip geometry. Distinguishes true MOVE (lengths match)
    /// from native STRETCH that left an incomplete station lattice on an expanded face.
    /// </summary>
    private static bool HipGeneratedRelativeCoverageMismatch(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry hipGeometry)
    {
        var existingIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            owner.Handle.ToString());
        if (existingIds.Count == 0)
        {
            return false;
        }

        if (!RoofGeneratedRafterSetService.TryRecoverRecipe(
                database,
                transaction,
                existingIds,
                out var recipe))
        {
            return true;
        }

        var layoutResult = RoofRafterLayoutSolver.Solve(
            hipGeometry,
            new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm));
        if (!layoutResult.IsValid || layoutResult.Layout is null)
        {
            return true;
        }

        if (layoutResult.Layout.Rafters.Count != existingIds.Count)
        {
            return true;
        }

        var expected = layoutResult.Layout.Rafters
            .Select(rafter => Math.Round(rafter.PlanLengthMm, 3, MidpointRounding.AwayFromZero))
            .OrderBy(length => length)
            .ToArray();
        var actual = new List<double>(existingIds.Count);
        foreach (var id in existingIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is not Line line)
            {
                return true;
            }

            var dx = line.EndPoint.X - line.StartPoint.X;
            var dy = line.EndPoint.Y - line.StartPoint.Y;
            actual.Add(Math.Round(
                Math.Sqrt(dx * dx + dy * dy),
                3,
                MidpointRounding.AwayFromZero));
        }

        actual.Sort();
        for (var index = 0; index < expected.Length; index++)
        {
            if (Math.Abs(expected[index] - actual[index]) >
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeUndoMark(Document document, string methodName)
    {
        try
        {
            var acadDocument = GetAcadDocument(document);
            if (acadDocument is null)
            {
                return false;
            }

            acadDocument.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod | ComInvoke,
                binder: null,
                target: acadDocument,
                args: null);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static object? GetAcadDocument(Document document)
    {
        var getter = document.GetType().GetMethod("GetAcadDocument", Type.EmptyTypes);
        if (getter?.Invoke(document, null) is { } fromDocument)
        {
            return fromDocument;
        }

        var acadApplication = AcApp.AcadApplication;
        return acadApplication?.GetType().InvokeMember(
            "ActiveDocument",
            BindingFlags.GetProperty | ComInvoke,
            binder: null,
            target: acadApplication,
            args: null);
    }

    private sealed record InspectionPlan(
        HashSet<ObjectId> RelatedIds,
        HashSet<ObjectId> ResizeOwnerIds,
        HashSet<ObjectId> UnsupportedOwnerIds,
        HashSet<ObjectId> DisplayTamperOwnerIds,
        HashSet<ObjectId> GeneratedMemberTamperOwnerIds,
        HashSet<ObjectId> SourceEraseOwnerIds,
        HashSet<ObjectId> GeneratedTimberEraseOwnerIds,
        HashSet<ObjectId> GeneratedAnnotationEraseOwnerIds);

    private enum ResizeApplyResult
    {
        Skipped = 0,
        Applied = 1,
        HardFailure = 2,
    }

    private enum UnsupportedRecoveryBatchResult
    {
        Unavailable = 0,
        RecoveredAll = 1,
        HardFailure = 2,
    }
}

#if DEBUG
internal static class RoofUndoGuardDiag
{
    public static void Write(
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string? command,
        string service)
    {
        if (editor is null)
        {
            return;
        }

        var normalized = LiveGeometryCommandRules.NormalizeCommandName(command).ToUpperInvariant();
        var line =
            "ROOF_UNDO_GUARD" +
            $" command={normalized}" +
            " action=skip-write" +
            $" service={service}" +
            " result=ok";
        try
        {
            editor.WriteMessage("\n" + line);
        }
        catch
        {
        }
    }
}
#endif
