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
/// Narrow live SimpleGable rectangular resize and display-cache repair on the existing
/// live-geometry path. Does not add a roof reactor, overrule, or deep-clone hook.
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
            !RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
        {
            return Array.Empty<ObjectId>();
        }

        try
        {
            var plan = Inspect(document.Database, modifiedIds, globalCommandName);
            if (plan.RelatedIds.Count == 0)
            {
                return Array.Empty<ObjectId>();
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

                if (LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName))
                {
                    var recovery = TryRecoverUnsupportedOwners(
                        document,
                        globalCommandName,
                        plan.UnsupportedOwnerIds);
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
                else
                {
                    document.Editor.WriteMessage(
                        UiStrings.GetString("Command_Roof_PersistedStale"));
                }
            }

            // Scenario 2: source remains RigidEquivalent; only generated timber/annotations
            // were stretched. Restore members from the command snapshot — never treat as
            // Unsupported source / footprint messaging.
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

            // Display-only STRETCH / GRIP_STRETCH: source path already handled this owner
            // when ResizeOwnerIds / UnsupportedOwnerIds contain it (precedence), including
            // deferred display-rebuild batches of the same command.
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
                LiveGeometryCommandRules.IsUndoGroupingSourceCommand(globalCommandName) &&
                ApplyDisplayTampers(document, displayTamperOwners, modifiedIds))
            {
                TransientNotificationService.Show(
                    "Command_Roof_DisplayTamperNotificationTitle",
                    "Command_Roof_DisplayTamperNotificationBody");
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
        string? globalCommandName)
    {
        var related = new HashSet<ObjectId>();
        var resizeOwners = new HashSet<ObjectId>();
        var unsupportedOwners = new HashSet<ObjectId>();
        var displayTamperCandidates = new HashSet<ObjectId>();
        var generatedMemberTamperCandidates = new HashSet<ObjectId>();
        using var transaction = database.TransactionManager.StartTransaction();
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

            if (entity is not Polyline polyline ||
                RoofDefinitionStore.Read(polyline).Data is null)
            {
                continue;
            }

            related.Add(id);
            switch (ClassifyOwner(polyline).Kind)
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

            // Only when the source footprint itself is still valid/rigid.
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    database) ||
                owner is null ||
                ClassifyOwner(owner).Kind != RoofSourceChangeKind.RigidEquivalent)
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
                    generatedMemberTamperOwners.Contains(ownerId))
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
            generatedMemberTamperOwners);
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

        if (!TryResolveAnnotationSourceHandle(entity, out var sourceHandle) ||
            string.IsNullOrWhiteSpace(sourceHandle))
        {
            return false;
        }

        if (!TryResolveHandleToEntity(database, transaction, sourceHandle, out var sourceEntity) ||
            sourceEntity is null)
        {
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
        return sourceTimber.Data is not null &&
               !string.IsNullOrWhiteSpace(sourceTimber.Data.RoofOwnerReference) &&
               TryResolveHandleToOwnerPolyline(
                   database,
                   transaction,
                   sourceTimber.Data.RoofOwnerReference,
                   out ownerId);
    }

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

        var classification = ClassifyOwner(owner);
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
        var generatedMemberCount = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            owner.Handle.ToString()).Count;

        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return ResizeApplyResult.Skipped;
        }

        var updated = RoofGeneratedMemberOverrideRules.PreserveEditState(
            RoofDefinitionPersistence.Create(
                input,
                validation.Footprint,
                classification.Geometry),
            RoofDefinitionStore.Read(owner).Data);
        RoofDefinitionStore.Write(owner, transaction, updated);
        var edges = SimpleGableRoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
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

        var rafterOutcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
            database,
            transaction,
            document.Editor,
            owner,
            classification.Geometry,
            TimberElementDefaultProfileStore.Load(),
            ElementLayerProfileStore.Load(),
            forceRegenerateOnSourceResize: true);
        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.Failed)
        {
            return ResizeApplyResult.HardFailure;
        }

        _ = RoofSourceResizeChildPolicyService.Apply(
            document,
            transaction,
            owner,
            rafterOutcome,
            generatedMemberCount);

        if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedAmbiguousRecipe)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_RecipeAmbiguous"));
        }
        else         if (rafterOutcome == RoofGeneratedRafterSetService.ReplacementOutcome.SkippedInvalidLayout)
        {
            document.Editor.WriteMessage(
                UiStrings.GetString("Command_RoofRafters_InvalidSpacing"));
        }

        RoofUnlockIndicatorService.Sync(database, transaction, owner);
#if DEBUG
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

    private static bool ApplyDisplayTampers(
        Document document,
        IReadOnlyCollection<ObjectId> ownerIds,
        IReadOnlyList<ObjectId> modifiedIds)
    {
        _ = modifiedIds;
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var wrote = false;
            foreach (var ownerId in ownerIds)
            {
                if (!TryApplyDisplayTamper(document.Database, transaction, ownerId))
                {
                    continue;
                }

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
        var classification = ClassifyOwner(owner);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent ||
            classification.Geometry is null)
        {
            return false;
        }

        var edges = SimpleGableRoofWireframe.Create(
            classification.Geometry,
            RoofPolylineExtractor.GetSourceElevation(owner));
        var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
        return RoofDisplayService.Rebuild(
            database,
            transaction,
            owner.ObjectId,
            owner.Handle.ToString(),
            edges,
            signature);
    }

    private static RoofSourceChangeClassification ClassifyOwner(Polyline polyline)
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

        return RoofDefinitionPersistence.Classify(
            input,
            validation.Footprint,
            stored.Data);
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
        HashSet<ObjectId> GeneratedMemberTamperOwnerIds);

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
