using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Accepts supported unlocked generated-timber edits or restores locked/unsupported
/// members through the existing command-scoped assembly snapshot.
/// </summary>
internal static class RoofGeneratedMemberManualEditService
{
    public static GeneratedMemberEditBatchResult ProcessOwners(
        Document document,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> ownerIds,
        IReadOnlyCollection<ObjectId> modifiedIds,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        var lockedAttempt = false;
        var unsupportedAttempt = false;
        var recovered = false;
        var accepted = false;
        foreach (var ownerId in ownerIds)
        {
            var outcome = ProcessOwner(
                document,
                globalCommandName,
                ownerId,
                modifiedIds,
                appendedTimberIds);
            switch (outcome)
            {
                case OwnerEditOutcome.LockedRecovered:
                    lockedAttempt = true;
                    recovered = true;
                    break;
                case OwnerEditOutcome.UnsupportedRecovered:
                    unsupportedAttempt = true;
                    recovered = true;
                    break;
                case OwnerEditOutcome.Accepted:
                    accepted = true;
                    break;
                case OwnerEditOutcome.Recovered:
                    recovered = true;
                    break;
            }
        }

        if (lockedAttempt)
        {
            TransientNotificationService.Show(
                "Command_Roof_LockedNotificationTitle",
                "Command_Roof_LockedNotificationBody");
        }
        else if (unsupportedAttempt)
        {
            TransientNotificationService.Show(
                "Command_Roof_UnsupportedMemberEditNotificationTitle",
                "Command_Roof_UnsupportedMemberEditNotificationBody");
        }

        if (recovered && !accepted)
        {
            return GeneratedMemberEditBatchResult.Recovered;
        }

        return accepted
            ? GeneratedMemberEditBatchResult.Accepted
            : GeneratedMemberEditBatchResult.None;
    }

    private static OwnerEditOutcome ProcessOwner(
        Document document,
        string? globalCommandName,
        ObjectId ownerId,
        IReadOnlyCollection<ObjectId> modifiedIds,
        IReadOnlyCollection<ObjectId> appendedTimberIds)
    {
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                    transaction,
                    ownerId,
                    OpenMode.ForRead,
                    out var owner,
                    document.Database) ||
                owner is null)
            {
                return OwnerEditOutcome.Skipped;
            }

            var stored = RoofDefinitionStore.Read(owner);
            if (stored.Data is null)
            {
                return OwnerEditOutcome.Skipped;
            }

            var sourceModified = modifiedIds.Contains(ownerId);
            var isRigidRoofTransform =
                sourceModified &&
                (LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
                     .Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
                 LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
                     .Equals("ROTATE", StringComparison.OrdinalIgnoreCase));
            if (isRigidRoofTransform)
            {
                owner.UpgradeOpen();
                RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
                transaction.Commit();
                return OwnerEditOutcome.Skipped;
            }

            var unlocked = stored.Data.EditState == RoofEditState.Unlocked;
            var supportedUnlocked =
                unlocked &&
                RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
                    globalCommandName);
            if (RoofLiveResizeService.ShouldSuppressIncidentalChildManualStretch(
                    ownerId,
                    globalCommandName))
            {
                owner.UpgradeOpen();
                RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
                transaction.Commit();
                return OwnerEditOutcome.Skipped;
            }

            if (!supportedUnlocked)
            {
                owner.UpgradeOpen();
                var recovered = RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(
                    document.Database,
                    transaction,
                    ownerId,
                    document.Editor);
                if (recovered == RoofUnsupportedStretchRecoveryOutcome.Recovered)
                {
                    if (unlocked)
                    {
                        WriteUnlockedReject(
                            document,
                            globalCommandName,
                            owner,
                            new ManualEditReject(
                                "command",
                                RoofGeneratedMemberEditCommandRules.IsClassicStretch(globalCommandName)
                                    ? "classic-stretch-locked"
                                    : "command-misclassified"));
                    }

                    transaction.Commit();
                    return unlocked
                        ? OwnerEditOutcome.UnsupportedRecovered
                        : OwnerEditOutcome.LockedRecovered;
                }

                if (recovered == RoofUnsupportedStretchRecoveryOutcome.Unavailable &&
                    LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)
                        .Equals("ERASE", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryRecoverErasedMembers(document, transaction, ownerId))
                    {
                        if (unlocked)
                        {
                            WriteUnlockedReject(
                                document,
                                globalCommandName,
                                owner,
                                new ManualEditReject("command", "command-misclassified"));
                        }

                        transaction.Commit();
                        return unlocked
                            ? OwnerEditOutcome.UnsupportedRecovered
                            : OwnerEditOutcome.LockedRecovered;
                    }
                }

                return OwnerEditOutcome.Skipped;
            }

            owner.UpgradeOpen();
            var accept = TryAcceptUnlockedEdits(
                document,
                transaction,
                owner,
                stored.Data,
                globalCommandName,
                modifiedIds,
                appendedTimberIds,
                out var reject);
            if (!accept)
            {
                WriteUnlockedReject(document, globalCommandName, owner, reject);
                var recovered = RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(
                    document.Database,
                    transaction,
                    ownerId,
                    document.Editor);
                if (recovered == RoofUnsupportedStretchRecoveryOutcome.Recovered ||
                    TryRecoverErasedMembers(document, transaction, ownerId))
                {
                    transaction.Commit();
                    return OwnerEditOutcome.UnsupportedRecovered;
                }

                return OwnerEditOutcome.Skipped;
            }

            RoofAttachedManualLifecycleService.RefreshModifiedAttachedManualRelatives(
                document,
                transaction,
                owner.Handle.ToString(),
                modifiedIds,
                globalCommandName);
            RefreshModifiedAttachedManualNumberingAndAnnotations(
                document,
                transaction,
                owner,
                modifiedIds);
            _ = RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, owner.ObjectId);
            RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
            transaction.Commit();
            return OwnerEditOutcome.Accepted;
        }
    }

    private static void RefreshModifiedAttachedManualNumberingAndAnnotations(
        Document document,
        Transaction transaction,
        Polyline owner,
        IReadOnlyCollection<ObjectId> modifiedIds)
    {
        var modifiedSet = new HashSet<ObjectId>(modifiedIds);
        var modifiedAttachedIds = RoofAttachedManualTimberStore.FindByOwner(
                document.Database,
                transaction,
                owner.Handle.ToString())
            .Where(modifiedSet.Contains)
            .ToList();
        if (modifiedAttachedIds.Count == 0)
        {
            return;
        }

        // Reuse the AK_RECALC pipeline (full-drawing numbering reconciliation + per-element
        // annotation refresh) so COPY AttachedManual children get the same signature /
        // ElementId / annotation semantics as manually edited Generated members.
        _ = ElementLabelService.UpdateInCurrentTransaction(
            document.Database,
            transaction,
            document.Editor,
            modifiedAttachedIds,
            modifiedAttachedIds);
    }

    private static bool TryAcceptUnlockedEdits(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofDefinitionData definition,
        string? globalCommandName,
        IReadOnlyCollection<ObjectId> modifiedIds,
        IReadOnlyCollection<ObjectId> appendedTimberIds,
        out ManualEditReject? reject)
    {
        reject = null;
        var input = RoofPolylineExtractor.Extract(owner);
        var validation = RoofFootprintValidator.Validate(input);
        if (!validation.IsValid || validation.Footprint is null)
        {
            reject = new ManualEditReject("footprint", "source-roof-not-rigid-equivalent");
            return false;
        }

        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(owner.ObjectId, out var snapshot))
        {
            reject = new ManualEditReject("snapshot", "no-command-snapshot");
            return false;
        }

        var restored = RoofDefinitionPersistence.Restore(input, validation.Footprint, definition);
        if (!restored.IsValid || restored.Geometry is null)
        {
            reject = new ManualEditReject("restore", "source-roof-not-rigid-equivalent");
            return false;
        }

        var elevation = RoofPolylineExtractor.GetSourceElevation(owner);
        var planeNormal = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;
        var overrides = new RoofManualOverrideSet(definition.Overrides);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var isErase = RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName);
        var isTrimOrExtend = RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand(
            globalCommandName);
        var isGrip = RoofGeneratedMemberEditCommandRules.IsGripStretchCommand(globalCommandName);
        var isMove = RoofGeneratedMemberEditCommandRules.IsMoveCommand(globalCommandName);
        var isRotate = RoofGeneratedMemberEditCommandRules.IsRotateCommand(globalCommandName);
        var isStretch = RoofGeneratedMemberEditCommandRules.IsClassicStretch(globalCommandName);
        var isBreak = RoofGeneratedMemberEditCommandRules.IsBreakCommand(globalCommandName);
        var isTargetedRecalc = RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand(
            globalCommandName);

        if (isErase)
        {
            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var presentationBatch = AutoCadAnnotationPresentationBatchContext.Create(
                document.Database,
                transaction,
                defaultProfile);
            var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
            var overrideChanged = false;
            var acceptedCount = 0;
            foreach (var timber in snapshot.Assembly.TimberLines)
            {
                if (TryIsLiveTimber(document.Database, timber.EntityHandle))
                {
                    if (HasErasedOwnedAnnotation(document.Database, snapshot.Assembly, timber.SourceHandle) &&
                        !TryRestoreLiveTimberAnnotations(
                            document,
                            transaction,
                            timber.EntityHandle,
                            presentationBatch,
                            roundingStepMm,
                            globalCommandName,
                            owner.Handle.ToString()))
                    {
                        reject = new ManualEditReject(
                            "annotation-refresh",
                            "annotation-refresh-failure",
                            timber.EntityHandle);
                        return false;
                    }

                    continue;
                }

                // AttachedManual ERASE (Origin.Copy OR Origin.Split) is a permanent
                // delete: accept the native erase, remove its annotations, and create
                // no Generated suppression override and no logical-key requirement.
                if (TryClassifyErasedCopyAttachedManual(
                        document.Database,
                        transaction,
                        timber.EntityHandle,
                        out var attachedData) &&
                    attachedData is not null)
                {
                    DeleteAnnotationsForHandle(
                        document.Database,
                        transaction,
                        timber.SourceHandle);
                    acceptedCount++;
                    var isSplit = attachedData.Origin == RoofAttachedManualOrigin.Split;
                    WriteAccept(
                        document,
                        globalCommandName,
                        owner.Handle.ToString(),
                        1,
                        "-",
                        isSplit ? "split-delete" : "copy-delete");
                    WriteAttachedManualErase(
                        document,
                        globalCommandName,
                        owner.Handle.ToString(),
                        timber.EntityHandle,
                        attachedData.Origin.ToString(),
                        "ok");
                    continue;
                }

                if (!TryResolveErasedMemberKey(
                        document.Database,
                        transaction,
                        timber.EntityHandle,
                        out var key,
                        out var elementId))
                {
                    reject = new ManualEditReject(
                        "member-resolve",
                        "logical-key-missing",
                        timber.EntityHandle);
                    return false;
                }

                overrides = overrides.Upsert(
                    RoofGeneratedMemberOverride.Suppress(key, elementId));
                DeleteAnnotationsForHandle(document.Database, transaction, timber.SourceHandle);
                overrideChanged = true;
                acceptedCount++;
                WriteAccept(
                    document,
                    globalCommandName,
                    owner.Handle.ToString(),
                    1,
                    FormatKey(key),
                    "suppress");
            }

            if (overrideChanged)
            {
                try
                {
                    var updated = RoofGeneratedMemberOverrideRules.WithEditState(
                        definition,
                        RoofEditState.Unlocked,
                        overrides.Items);
                    RoofDefinitionStore.Write(owner, transaction, updated);
                }
                catch (System.Exception)
                {
                    reject = new ManualEditReject("persist", "persistence-codec-failure");
                    return false;
                }
            }

            if (acceptedCount == 0 && !overrideChanged)
            {
                WriteAccept(
                    document,
                    globalCommandName,
                    owner.Handle.ToString(),
                    0,
                    "-",
                    "annotation-restore");
            }

            return true;
        }
        else
        {
            var generatedIds = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                owner.Handle.ToString());
            var defaultProfile = TimberElementDefaultProfileStore.Load();
            var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
            var standaloneIds = new List<ObjectId>();
            var keepStandalones = false;
            if (!TryPromoteSplitFragments(
                    document,
                    transaction,
                    owner,
                    snapshot,
                    generatedIds,
                    modifiedIds,
                    appendedTimberIds,
                    metadataStore,
                    globalCommandName,
                    out standaloneIds,
                    out reject))
            {
                TryEraseStandaloneFragments(document.Database, transaction, standaloneIds);
                return false;
            }

            try
            {
            generatedIds = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                owner.Handle.ToString());
            var changedRecalcItems = new List<RoofGeneratedMemberRecalcItem>();
            // Maps a recalculated Generated member ObjectId to its logical key, so the
            // persisted override's ReservedElementId can be synchronized to the FINAL
            // reconciled ElementId after recalc (a length-changing accepted edit must not
            // leave a stale pre-recalc reservation that a later rebuild would replay).
            var recalcKeyById = new Dictionary<ObjectId, RoofGeneratedMemberKey>();
            foreach (var id in generatedIds)
            {
                if (!modifiedIds.Contains(id))
                {
                    continue;
                }

                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForWrite,
                        out var entity,
                        document.Database) ||
                    entity is null)
                {
                    reject = new ManualEditReject(
                        "member-resolve",
                        "live-member-missing",
                        id.Handle.ToString());
                    return false;
                }

                if (entity is not Line line)
                {
                    reject = new ManualEditReject(
                        "member-resolve",
                        "unsupported-entity-type",
                        entity.Handle.ToString());
                    return false;
                }

                var generated = RoofGeneratedTimberStore.Read(line);
                if (generated.Data is null)
                {
                    reject = new ManualEditReject(
                        "member-resolve",
                        "logical-key-missing",
                        line.Handle.ToString());
                    return false;
                }

                if (!metadataStore.TryRead(line, out var timberData) || timberData is null)
                {
                    reject = new ManualEditReject(
                        "member-resolve",
                        "timber-metadata-missing",
                        line.Handle.ToString(),
                        FormatKey(RoofGeneratedMemberKey.From(generated.Data)));
                    return false;
                }

                var key = RoofGeneratedMemberKey.From(generated.Data);
                if (!TryCanonicalGeometry(
                        restored.Geometry,
                        generated.Data,
                        timberData.WidthMm,
                        elevation,
                        out var canonical))
                {
                    reject = new ManualEditReject(
                        "canonical-geometry",
                        "live-member-missing",
                        line.Handle.ToString(),
                        FormatKey(key),
                        after: FormatLine(line));
                    return false;
                }

                var rawObserved = ToGeometry(line);
                if (!RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, planeNormal, out var basis))
                {
                    reject = new ManualEditReject(
                        "normalize",
                        "basis-failed",
                        line.Handle.ToString(),
                        FormatKey(key));
                    return false;
                }

                var observed = RoofGeneratedMemberOverrideMath.NormalizeToBasis(rawObserved, basis, out var maxZDelta);
                if (maxZDelta > RoofGeneratedMemberOverrideMath.LengthToleranceMm)
                {
                    WriteNormalize(document, globalCommandName, owner.Handle.ToString(), line.Handle.ToString(), maxZDelta, basis.Origin.Z);
                }

                RoofGeneratedMemberOverride? existing = null;
                if (overrides.TryGet(key, out var existingValue))
                {
                    existing = existingValue;
                }

                if (!RoofGeneratedMemberOverrideMath.TryApply(
                        canonical,
                        planeNormal,
                        existing,
                        out var baseline))
                {
                    WriteComposeFail(
                        document,
                        globalCommandName,
                        owner.Handle.ToString(),
                        line.Handle.ToString(),
                        FormatKey(key),
                        canonical,
                        canonical,
                        observed,
                        existing,
                        new RoofGeneratedMemberComposeFailure(
                            "existing-apply",
                            "override-composition-failure",
                            canonical,
                            observed,
                            existing?.RotationRadians ?? 0d,
                            existing?.AlongMm ?? 0d,
                            existing?.LateralMm ?? 0d,
                            existing?.StartOffsetMm ?? 0d,
                            existing?.EndOffsetMm ?? 0d,
                            existing?.RotationRadians ?? 0d,
                            existing?.AlongMm ?? 0d,
                            existing?.LateralMm ?? 0d,
                            canonical,
                            false,
                            -1d));
                    reject = new ManualEditReject(
                        "compose",
                        "override-composition-failure",
                        line.Handle.ToString(),
                        FormatKey(key),
                        FormatGeometry(canonical),
                        FormatGeometry(observed));
                    return false;
                }

                if (!TryClassifyAcceptedMemberEdit(
                        document,
                        globalCommandName,
                        owner.Handle.ToString(),
                        isTrimOrExtend && standaloneIds.Count == 0,
                        isGrip,
                        isMove,
                        isRotate,
                        isStretch || isBreak || standaloneIds.Count > 0,
                        key,
                        timberData.ElementId,
                        canonical,
                        baseline,
                        observed,
                        planeNormal,
                        existing,
                        line,
                        out var overrideData,
                        out var acceptedGeometry,
                        out var unchanged,
                        out reject))
                {
                    return false;
                }

                if (unchanged)
                {
                    ApplyAcceptedLineGeometry(line, baseline, baseline);
                    continue;
                }

                overrides = overrideData is null
                    ? overrides.Remove(key)
                    : overrides.Upsert(overrideData);
                ApplyAcceptedLineGeometry(line, acceptedGeometry, baseline);

                if (RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(
                        baseline,
                        acceptedGeometry))
                {
                    recalcKeyById[id] = key;
                    changedRecalcItems.Add(new RoofGeneratedMemberRecalcItem(
                        id,
                        line.Handle.ToString(),
                        timberData.ElementId,
                        RoofGeneratedMemberRecalcScopeRules.SignatureFrom(
                            timberData,
                            baseline.LengthMm,
                            roundingStepMm),
                        RoofGeneratedMemberRecalcScopeRules.SignatureFrom(
                            timberData,
                            acceptedGeometry.LengthMm,
                            roundingStepMm)));
                }
            }

            if (isTargetedRecalc)
            {
                if (!TryRecalculateAcceptedMembers(
                        document,
                        transaction,
                        owner,
                        globalCommandName,
                        changedRecalcItems,
                        out reject))
                {
                    return false;
                }

                WriteAccept(
                    document,
                    globalCommandName,
                    owner.Handle.ToString(),
                    changedRecalcItems.Count,
                    "-",
                    "ok");
            }

            // A length-changing accepted edit renumbers the member via recalc; the
            // override captured earlier still carries the PRE-recalc ElementId as its
            // reservation. Synchronize it to the FINAL reconciled ElementId so a later
            // SupportedResize rebuild cannot replay a stale number belonging to a
            // different signature. Fixes the shared Generated-manual-edit path
            // (BREAK, split-TRIM, TRIM, EXTEND, endpoint GRIP_STRETCH).
            if (isTargetedRecalc && changedRecalcItems.Count > 0)
            {
                overrides = SyncReservedElementIdsAfterRecalc(
                    overrides,
                    document,
                    transaction,
                    metadataStore,
                    changedRecalcItems,
                    recalcKeyById,
                    globalCommandName,
                    owner);
            }

            if (!TryFinalizeStandaloneFragments(
                    document,
                    transaction,
                    standaloneIds,
                    metadataStore,
                    defaultProfile,
                    out reject))
            {
                return false;
            }

            try
            {
                var updated = RoofGeneratedMemberOverrideRules.WithEditState(
                    definition,
                    RoofEditState.Unlocked,
                    overrides.Items);
                RoofDefinitionStore.Write(owner, transaction, updated);
            }
            catch (System.Exception)
            {
                reject = new ManualEditReject("persist", "persistence-codec-failure");
                return false;
            }

            keepStandalones = true;
            return true;
            }
            finally
            {
                if (!keepStandalones)
                {
                    TryEraseStandaloneFragments(document.Database, transaction, standaloneIds);
                }
            }
        }
    }

    private static bool TryClassifyAcceptedMemberEdit(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        bool isTrimOrExtend,
        bool isGrip,
        bool isMove,
        bool isRotate,
        bool isFreeformRepresentable,
        RoofGeneratedMemberKey key,
        string? reservedElementId,
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        RoofPoint3D planeNormal,
        RoofGeneratedMemberOverride? existing,
        Line line,
        out RoofGeneratedMemberOverride? overrideData,
        out RoofGeneratedMemberGeometry acceptedGeometry,
        out bool unchanged,
        out ManualEditReject? reject)
    {
        overrideData = existing;
        acceptedGeometry = baseline;
        unchanged = false;
        reject = null;
        ManualEditReject Fail(RoofGeneratedMemberManualEditReason reason) =>
            new(
                "classify",
                RoofGeneratedMemberOverrideMath.ToReasonToken(reason),
                line.Handle.ToString(),
                FormatKey(key),
                FormatGeometry(baseline),
                FormatGeometry(observed));

        if (isTrimOrExtend)
        {
            if (!RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
                    baseline,
                    observed,
                    planeNormal,
                    out var startDelta,
                    out var endDelta,
                    out acceptedGeometry,
                    out var classifyReason))
            {
                reject = Fail(classifyReason);
                return false;
            }

            if (classifyReason == RoofGeneratedMemberManualEditReason.NeitherEndpointChanged)
            {
                unchanged = true;
                acceptedGeometry = baseline;
                return true;
            }

            overrideData = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
                existing,
                key,
                reservedElementId,
                startDelta,
                endDelta);
            return true;
        }

        if (isMove)
        {
            if (!RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
                    baseline,
                    observed,
                    planeNormal,
                    out var translation,
                    out acceptedGeometry,
                    out var moveReason))
            {
                reject = Fail(moveReason);
                return false;
            }

            if (moveReason == RoofGeneratedMemberManualEditReason.NeitherEndpointChanged)
            {
                unchanged = true;
                acceptedGeometry = baseline;
                return true;
            }

            if (!RoofGeneratedMemberOverrideMath.TryDecomposeInPlane(
                    canonical,
                    planeNormal,
                    translation,
                    out var alongDelta,
                    out var lateralDelta))
            {
                reject = Fail(RoofGeneratedMemberManualEditReason.OffPlane);
                return false;
            }

            overrideData = RoofGeneratedMemberOverrideMath.ComposeTranslation(
                existing,
                key,
                reservedElementId,
                alongDelta,
                lateralDelta);
            return true;
        }

        if (isRotate)
        {
            if (!RoofGeneratedMemberOverrideMath.TryClassifyRigidEqualLength(
                    baseline,
                    observed,
                    planeNormal,
                    out acceptedGeometry,
                    out var rotateReason))
            {
                reject = Fail(rotateReason);
                return false;
            }

            if (rotateReason == RoofGeneratedMemberManualEditReason.NeitherEndpointChanged)
            {
                unchanged = true;
                acceptedGeometry = baseline;
                return true;
            }

            if (!RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
                    canonical,
                    acceptedGeometry,
                    planeNormal,
                    key,
                    existing,
                    existing?.ReservedElementId ?? reservedElementId,
                    out overrideData,
                    out var composeFailure))
            {
                WriteComposeFail(
                    document,
                    globalCommandName,
                    ownerHandle,
                    line.Handle.ToString(),
                    FormatKey(key),
                    canonical,
                    baseline,
                    observed,
                    existing,
                    composeFailure);
                reject = new ManualEditReject(
                    "compose",
                    "override-composition-failure",
                    line.Handle.ToString(),
                    FormatKey(key),
                    FormatGeometry(baseline),
                    FormatGeometry(observed));
                return false;
            }

            return true;
        }

        if (isGrip || isFreeformRepresentable)
        {
            if (RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
                    baseline,
                    observed,
                    planeNormal,
                    out var startDelta,
                    out var endDelta,
                    out acceptedGeometry,
                    out var gripReason) &&
                (gripReason == RoofGeneratedMemberManualEditReason.Accepted ||
                 gripReason == RoofGeneratedMemberManualEditReason.NeitherEndpointChanged))
            {
                if (gripReason == RoofGeneratedMemberManualEditReason.NeitherEndpointChanged)
                {
                    unchanged = true;
                    acceptedGeometry = baseline;
                    return true;
                }

                overrideData = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
                    existing,
                    key,
                    reservedElementId,
                    startDelta,
                    endDelta);
                return true;
            }

            if (RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
                    baseline,
                    observed,
                    planeNormal,
                    out var translation,
                    out acceptedGeometry,
                    out var moveReason) &&
                moveReason == RoofGeneratedMemberManualEditReason.Accepted)
            {
                if (!RoofGeneratedMemberOverrideMath.TryDecomposeInPlane(
                        canonical,
                        planeNormal,
                        translation,
                        out var alongDelta,
                        out var lateralDelta))
                {
                    reject = Fail(RoofGeneratedMemberManualEditReason.OffPlane);
                    return false;
                }

                overrideData = RoofGeneratedMemberOverrideMath.ComposeTranslation(
                    existing,
                    key,
                    reservedElementId,
                    alongDelta,
                    lateralDelta);
                return true;
            }

            if (RoofGeneratedMemberOverrideMath.TryClassifyRigidEqualLength(
                    baseline,
                    observed,
                    planeNormal,
                    out acceptedGeometry,
                    out var rigidReason) &&
                rigidReason == RoofGeneratedMemberManualEditReason.Accepted)
            {
                if (!RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
                        canonical,
                        acceptedGeometry,
                        planeNormal,
                        key,
                        existing,
                        existing?.ReservedElementId ?? reservedElementId,
                        out overrideData,
                        out var gripComposeFailure))
                {
                    WriteComposeFail(
                        document,
                        globalCommandName,
                        ownerHandle,
                        line.Handle.ToString(),
                        FormatKey(key),
                        canonical,
                        baseline,
                        observed,
                        existing,
                        gripComposeFailure);
                    reject = Fail(
                        isFreeformRepresentable && !isGrip
                            ? RoofGeneratedMemberManualEditReason.UnrepresentableStretch
                            : RoofGeneratedMemberManualEditReason.UnsupportedGrip);
                    return false;
                }

                return true;
            }

            if (RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, planeNormal, out var gripBasis))
            {
                if (!RoofGeneratedMemberOverrideMath.TryProjectObserved(
                        observed,
                        gripBasis,
                        out var projected,
                        out var projectReason))
                {
                    reject = Fail(projectReason);
                    return false;
                }

                if (RoofGeneratedMemberOverrideMath.TryClassify(
                        canonical,
                        projected,
                        planeNormal,
                        key,
                        existing?.ReservedElementId ?? reservedElementId,
                        out overrideData) &&
                    RoofGeneratedMemberOverrideMath.TryApply(
                        canonical,
                        planeNormal,
                        overrideData,
                        out acceptedGeometry))
                {
                    return true;
                }
            }

            reject = Fail(
                isFreeformRepresentable && !isGrip
                    ? RoofGeneratedMemberManualEditReason.UnrepresentableStretch
                    : RoofGeneratedMemberManualEditReason.UnsupportedGrip);
            return false;
        }

        reject = new ManualEditReject(
            "classify",
            "command-misclassified",
            line.Handle.ToString(),
            FormatKey(key),
            FormatGeometry(canonical),
            FormatGeometry(observed));
        return false;
    }

    private static bool HasErasedOwnedAnnotation(
        Database database,
        RoofUnsupportedStretchAssemblySnapshotData assembly,
        string sourceHandle)
    {
        foreach (var annotation in assembly.Annotations)
        {
            if (!string.Equals(annotation.SourceHandle, sourceHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsHandleErased(database, annotation.EntityHandle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHandleErased(Database database, string handleText)
    {
        try
        {
            if (!long.TryParse(
                    handleText,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var handleValue))
            {
                return false;
            }

            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            return !id.IsNull && id.IsErased;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryRestoreLiveTimberAnnotations(
        Document document,
        Transaction transaction,
        string entityHandle,
        AutoCadAnnotationPresentationBatchContext presentationBatch,
        double roundingStepMm,
        string? globalCommandName,
        string ownerHandle)
    {
        try
        {
            if (!long.TryParse(
                    entityHandle,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var handleValue))
            {
                return false;
            }

            var id = document.Database.GetObjectId(false, new Handle(handleValue), 0);
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null)
            {
                return false;
            }

            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            if (!metadataStore.TryRead(entity, out var timberData) || timberData is null)
            {
                return false;
            }

            return TryRefreshAcceptedMemberAnnotations(
                document,
                transaction,
                entity,
                timberData,
                presentationBatch,
                roundingStepMm,
                globalCommandName,
                ownerHandle,
                "-");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static void WriteAccept(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        int changed,
        string? key,
        string action)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteAccept(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            changed,
            key,
            action);
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = changed;
        _ = key;
        _ = action;
#endif
    }

    private static void WriteAttachedManualErase(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        string timberHandle,
        string origin,
        string result)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteAttachedManualErase(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            timberHandle,
            origin,
            result);
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = timberHandle;
        _ = origin;
        _ = result;
#endif
    }

    private static void WriteNormalize(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        string timberHandle,
        double rawZDelta,
        double planeZ)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteNormalize(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            timberHandle,
            rawZDelta,
            planeZ);
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = timberHandle;
        _ = rawZDelta;
        _ = planeZ;
#endif
    }

    private static void WriteComposeFail(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        string timberHandle,
        string key,
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        RoofGeneratedMemberOverride? existing,
        RoofGeneratedMemberComposeFailure? failure)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteComposeFail(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            timberHandle,
            key,
            failure?.Stage ?? "compose",
            FormatGeometry(canonical),
            FormatGeometry(baseline),
            FormatGeometry(observed),
            existing?.RotationRadians ?? 0d,
            existing?.AlongMm ?? 0d,
            existing?.LateralMm ?? 0d,
            existing?.StartOffsetMm ?? 0d,
            existing?.EndOffsetMm ?? 0d,
            failure?.CandidateRotationRadians ?? existing?.RotationRadians ?? 0d,
            failure?.CandidateAlongMm ?? existing?.AlongMm ?? 0d,
            failure?.CandidateLateralMm ?? existing?.LateralMm ?? 0d,
            failure is { HasReplay: true } ? FormatGeometry(failure.Replay) : "-",
            failure?.MaxErrorMm ?? -1d,
            failure?.Reason ?? "override-composition-failure");
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = timberHandle;
        _ = key;
        _ = canonical;
        _ = baseline;
        _ = observed;
        _ = existing;
        _ = failure;
#endif
    }

    private static bool TryRecalculateAcceptedMembers(
        Document document,
        Transaction transaction,
        Polyline owner,
        string? globalCommandName,
        IReadOnlyList<RoofGeneratedMemberRecalcItem> changedItems,
        out ManualEditReject? reject)
    {
        reject = null;
        if (changedItems.Count == 0)
        {
            return true;
        }

        if (!RoofGeneratedMemberTargetedRecalcService.TryRecalculate(
                document,
                transaction,
                globalCommandName,
                owner.Handle.ToString(),
                changedItems,
                out var stage,
                out var reason,
                out var failHandle,
                out var error))
        {
            WriteRecalcFail(
                document,
                globalCommandName,
                owner.Handle.ToString(),
                failHandle,
                stage,
                error is null
                    ? reason
                    : ClassifyAnnotationRefreshException(error, stage));
            if (error is not null)
            {
                WriteAnnotationRefreshFail(
                    document,
                    globalCommandName,
                    owner.Handle.ToString(),
                    failHandle ?? "-",
                    "-",
                    "-",
                    ClassifyAnnotationKind(error),
                    stage,
                    ClassifyAnnotationRefreshException(error, stage),
                    error);
            }

            reject = new ManualEditReject(
                "recalc",
                "targeted-recalc-failure",
                failHandle);
            return false;
        }

        return true;
    }

    // After an accepted targeted recalc, update each recalculated Generated member's
    // override reservation (ReservedElementId) to its FINAL reconciled ElementId. The
    // override was composed earlier with the pre-recalc ElementId; leaving it stale would
    // let a later SupportedResize rebuild force an obsolete number that collides with a
    // different signature's item group.
    private static RoofManualOverrideSet SyncReservedElementIdsAfterRecalc(
        RoofManualOverrideSet overrides,
        Document document,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<RoofGeneratedMemberRecalcItem> changedItems,
        IReadOnlyDictionary<ObjectId, RoofGeneratedMemberKey> keyById,
        string? globalCommandName,
        Polyline owner)
    {
        foreach (var item in changedItems)
        {
            if (!RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
                    item.OldSignature,
                    item.NewSignature) ||
                !keyById.TryGetValue(item.Id, out var key) ||
                !overrides.TryGet(key, out var current) ||
                item.Id.IsNull ||
                item.Id.IsErased ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    item.Id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var finalData) ||
                finalData is null)
            {
                continue;
            }

            var finalElementId = finalData.ElementId;
            if (string.Equals(
                    current.ReservedElementId,
                    finalElementId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overrides = overrides.Upsert(current with { ReservedElementId = finalElementId });
#if DEBUG
            RoofGeneratedMemberManualEditDiag.WriteIdentitySync(
                document.Editor,
                item.Handle,
                FormatKey(key),
                current.ReservedElementId,
                finalElementId,
                finalElementId,
                "ok");
#endif
        }

        return overrides;
    }

    private static bool TryRefreshAcceptedMemberAnnotations(
        Document document,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData timberData,
        AutoCadAnnotationPresentationBatchContext presentationBatch,
        double roundingStepMm,
        string? globalCommandName,
        string ownerHandle,
        string key)
    {
        try
        {
            // EnsureForElement returns true only when a new main label is created.
            // Updating the existing MLeader/DBText/Polyline set (accepted TRIM) returns
            // false, which AK_LABEL counts as "updated". Treating that as failure
            // aborted HOST H3-R2 after classification already succeeded.
            _ = TimberAnnotationService.EnsureForElement(
                document.Database,
                transaction,
                sourceEntity,
                timberData,
                presentationBatch,
                roundingStepMm: roundingStepMm);
            return true;
        }
        catch (System.Exception ex)
        {
            WriteAnnotationRefreshFail(
                document,
                globalCommandName,
                ownerHandle,
                sourceEntity.Handle.ToString(),
                key,
                "-",
                ClassifyAnnotationKind(ex),
                "ensure-for-element",
                ClassifyAnnotationRefreshException(ex, "ensure-for-element"),
                ex);
            return false;
        }
    }

    private static string ClassifyAnnotationKind(System.Exception ex)
    {
        var text = AnnotationExceptionText(ex);
        if (ContainsOrdinal(text, "MLeader"))
        {
            return "MLeader";
        }

        if (ContainsOrdinal(text, "DBText"))
        {
            return "DBText";
        }

        if (ContainsOrdinal(text, "Polyline"))
        {
            return "Polyline";
        }

        if (ContainsOrdinal(text, "MText"))
        {
            return "MText";
        }

        return "EnsureForElement";
    }

    private static string ClassifyAnnotationRefreshException(System.Exception ex, string stage)
    {
        var text = AnnotationExceptionText(ex);
        if (ContainsOrdinal(text, "eWasErased") ||
            ContainsOrdinal(text, "eIsErased") ||
            ContainsOrdinal(text, "ObjectDisposedException"))
        {
            return "disposed-erased-object";
        }

        if (ContainsOrdinal(text, "eAlreadyOpen") ||
            ContainsOrdinal(text, "eOnLockedLayer"))
        {
            return "transaction-open-mode-failure";
        }

        if (ContainsOrdinal(text, "MLeader"))
        {
            return "MLeader-write-failure";
        }

        if (ContainsOrdinal(text, "DBText"))
        {
            return "DBText-write-failure";
        }

        if (ContainsOrdinal(text, "Polyline"))
        {
            return "Polyline-write-failure";
        }

        if (ContainsOrdinal(text, "SourceHandle"))
        {
            return "SourceHandle-mismatch";
        }

        _ = stage;
        if (ex is InvalidOperationException || ex is ArgumentOutOfRangeException)
        {
            return "geometry-calculation-failure";
        }

        return "ensure-for-element-exception";
    }

    private static string AnnotationExceptionText(System.Exception ex)
    {
        var text = ex.GetType().Name + " " + ex.Message;
        if (ex.InnerException is not null)
        {
            text += " " + ex.InnerException.GetType().Name + " " + ex.InnerException.Message;
        }

        return text;
    }

    private static bool ContainsOrdinal(string text, string value) =>
        text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void WriteAnnotationRefreshFail(
        Document document,
        string? globalCommandName,
        string? owner,
        string? timber,
        string? key,
        string? annotation,
        string kind,
        string stage,
        string reason,
        System.Exception ex)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteAnnotationFail(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            owner,
            timber,
            key,
            annotation,
            kind,
            stage,
            reason,
            ex.GetType().Name + ":" + ex.Message,
            "failed");
#else
        _ = document;
        _ = globalCommandName;
        _ = owner;
        _ = timber;
        _ = key;
        _ = annotation;
        _ = kind;
        _ = stage;
        _ = reason;
        _ = ex;
#endif
    }

    private static void WriteRecalcFail(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        string? timberHandle,
        string stage,
        string reason)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.WriteRecalcFail(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            ownerHandle,
            timberHandle,
            stage,
            reason);
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = timberHandle;
        _ = stage;
        _ = reason;
#endif
    }

    private static void RestoreModifiedAnnotationsFromTimber(
        Document document,
        Transaction transaction,
        RoofUnsupportedStretchAssemblySnapshotData assembly,
        IReadOnlyList<ObjectId> generatedIds)
    {
        _ = assembly;
        _ = generatedIds;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var batch = AutoCadAnnotationPresentationBatchContext.Create(
            document.Database,
            transaction,
            defaultProfile);
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(
                     document.Database,
                     transaction,
                     assembly.RoofSource.OwnerHandle))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    document.Database) ||
                entity is null ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            _ = TimberAnnotationService.EnsureForElement(
                document.Database,
                transaction,
                entity,
                data,
                batch,
                roundingStepMm: defaultProfile.GetCuttingLengthRoundingStepMm());
        }
    }

    private static bool TryPromoteSplitFragments(
        Document document,
        Transaction transaction,
        Polyline owner,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry snapshot,
        IReadOnlyList<ObjectId> generatedIds,
        IReadOnlyCollection<ObjectId> modifiedIds,
        IReadOnlyCollection<ObjectId> appendedTimberIds,
        AutoCadTimberElementMetadataStore metadataStore,
        string? globalCommandName,
        out List<ObjectId> standaloneIds,
        out ManualEditReject? reject)
    {
        standaloneIds = [];
        reject = null;
        if (!RoofGeneratedMemberEditCommandRules.IsSplitCommand(globalCommandName))
        {
            return true;
        }

        var snapshotHandles = snapshot.Assembly.TimberLines
            .Select(item => item.EntityHandle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appendedHandles = appendedTimberIds
            .Select(id => id.Handle.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshotGeometry = snapshot.Assembly.TimberLines.ToDictionary(
            item => item.EntityHandle,
            item => ToSnapshotGeometry(item),
            StringComparer.OrdinalIgnoreCase);
        var liveByKey = new Dictionary<RoofGeneratedMemberKey, List<Line>>();
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    document.Database) ||
                line is null)
            {
                continue;
            }

            var generated = RoofGeneratedTimberStore.Read(line);
            if (generated.Data is null)
            {
                continue;
            }

            var key = RoofGeneratedMemberKey.From(generated.Data);
            if (!liveByKey.TryGetValue(key, out var list))
            {
                list = [];
                liveByKey[key] = list;
            }

            list.Add(line);
        }

        foreach (var pair in liveByKey)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            Line? generatedFragment = null;
            var extras = new List<Line>();
            var liveHandles = pair.Value.Select(line => line.Handle.ToString()).ToArray();
            if (!RoofGeneratedMemberSplitIdentityRules.TryResolveFragments(
                    liveHandles,
                    snapshotHandles,
                    appendedHandles,
                    out var generatedHandle,
                    out var standaloneHandles))
            {
                reject = new ManualEditReject(
                    "split",
                    "duplicate-generated-key",
                    pair.Value[0].Handle.ToString(),
                    FormatKey(pair.Key));
                return false;
            }

            foreach (var line in pair.Value)
            {
                if (string.Equals(
                        line.Handle.ToString(),
                        generatedHandle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    generatedFragment = line;
                }
                else if (standaloneHandles.Any(handle =>
                             string.Equals(
                                 handle,
                                 line.Handle.ToString(),
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    extras.Add(line);
                }
            }

            if (generatedFragment is null || extras.Count == 0)
            {
                reject = new ManualEditReject(
                    "split",
                    "duplicate-generated-key",
                    pair.Value[0].Handle.ToString(),
                    FormatKey(pair.Key));
                return false;
            }

            if (!metadataStore.TryRead(generatedFragment, out var sourceData) || sourceData is null)
            {
                reject = new ManualEditReject(
                    "split",
                    "timber-metadata-missing",
                    generatedFragment.Handle.ToString(),
                    FormatKey(pair.Key));
                return false;
            }

            foreach (var extra in extras)
            {
                if (!TryAttachManualSplitFragment(
                        document,
                        transaction,
                        extra,
                        sourceData,
                        metadataStore,
                        owner.Handle.ToString(),
                        generatedFragment.Handle.ToString(),
                        extra.Handle.ToString(),
                        globalCommandName,
                        out reject))
                {
                    return false;
                }

                standaloneIds.Add(extra.ObjectId);
            }
        }

        foreach (var id in modifiedIds)
        {
            if (generatedIds.Contains(id) || standaloneIds.Contains(id))
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var candidate,
                    document.Database) ||
                candidate is null ||
                RoofGeneratedTimberStore.Read(candidate).Data is not null)
            {
                continue;
            }

            var fragment = ToGeometry(candidate);
            Line? sourceLine = null;
            TimberElementData? sourceData = null;
            string? sourceHandle = null;
            foreach (var timber in snapshot.Assembly.TimberLines)
            {
                if (!snapshotGeometry.TryGetValue(timber.EntityHandle, out var parent))
                {
                    continue;
                }

                if (!RoofGeneratedMemberSplitRules.IsCollinearFragment(
                        parent,
                        fragment,
                        RoofGeneratedMemberOverrideMath.LengthToleranceMm))
                {
                    continue;
                }

                if (!TryOpenSnapshotLine(
                        document.Database,
                        transaction,
                        timber.EntityHandle,
                        out var liveSource) ||
                    liveSource is null ||
                    !metadataStore.TryRead(liveSource, out sourceData) ||
                    sourceData is null)
                {
                    continue;
                }

                sourceLine = liveSource;
                sourceHandle = timber.EntityHandle;
                break;
            }

            if (sourceLine is null || sourceData is null || sourceHandle is null)
            {
                continue;
            }

            if (!TryAttachManualSplitFragment(
                    document,
                    transaction,
                    candidate,
                    sourceData,
                    metadataStore,
                    owner.Handle.ToString(),
                    sourceHandle,
                    candidate.Handle.ToString(),
                    globalCommandName,
                    out reject))
            {
                return false;
            }

            standaloneIds.Add(candidate.ObjectId);
        }

        return true;
    }

    private static bool TryAttachManualSplitFragment(
        Document document,
        Transaction transaction,
        Line extra,
        TimberElementData sourceData,
        AutoCadTimberElementMetadataStore metadataStore,
        string ownerHandle,
        string generatedHandle,
        string attachedManualHandle,
        string? globalCommandName,
        out ManualEditReject? reject)
    {
        reject = null;
        extra.UpgradeOpen();
        if (!RoofGeneratedTimberStore.TryClear(extra, transaction, out var clearReason))
        {
            reject = new ManualEditReject("split", clearReason, extra.Handle.ToString());
            return false;
        }

        if (RoofGeneratedTimberStore.Read(extra).Data is not null)
        {
            reject = new ManualEditReject("split", "duplicate-generated-key", extra.Handle.ToString());
            return false;
        }

        if (!metadataStore.TryRead(extra, out var existing) || existing is null)
        {
            metadataStore.Write(extra, sourceData);
        }

        RoofAttachedManualTimberData attachedData;
        var sourceRole = "Generated";
        var resolvedAnchorKey = (RoofGeneratedMemberKey?)null;
        var anchorStart = Point3d.Origin;
        var anchorEnd = Point3d.Origin;
        if (TryOpenSnapshotLine(document.Database, transaction, generatedHandle, out var anchorLine) &&
            anchorLine is not null)
        {
            var generated = RoofGeneratedTimberStore.Read(anchorLine).Data;
            if (generated is not null)
            {
                // BREAK of a Generated member: the surviving Generated fragment is the
                // exact anchor. (Existing HOST PASS path, unchanged.)
                sourceRole = "Generated";
                resolvedAnchorKey = RoofGeneratedMemberKey.From(generated);
                anchorStart = anchorLine.StartPoint;
                anchorEnd = anchorLine.EndPoint;
            }
            else if (RoofAttachedManualTimberStore.Read(anchorLine).Data is
                     { Origin: RoofAttachedManualOrigin.Split } splitSource &&
                     splitSource.AnchorGeneratedMemberKey is { } splitAnchorKey &&
                     RoofAttachedManualLifecycleService.TryFindGeneratedAnchorLine(
                         document.Database,
                         transaction,
                         ownerHandle,
                         splitAnchorKey,
                         out var generatedAnchorLine) &&
                     generatedAnchorLine is not null)
            {
                // BREAK of an existing AttachedManual Origin.Split child: no new Generated
                // member is produced. Both resulting fragments keep the SOURCE's exact
                // persisted anchor (BREAK is not MOVE — no nearest re-anchor) and
                // Origin.Split; their independent RelativeSegments encode their separate
                // geometry.
                sourceRole = "AttachedManual";
                resolvedAnchorKey = splitAnchorKey;
                anchorStart = generatedAnchorLine.StartPoint;
                anchorEnd = generatedAnchorLine.EndPoint;
            }
        }

        if (resolvedAnchorKey is { } resolvedAnchor)
        {
            attachedData = RoofAttachedManualLifecycleService.CreateAnchoredData(
                ownerHandle,
                attachedManualHandle,
                resolvedAnchor,
                anchorStart,
                anchorEnd,
                extra.StartPoint,
                extra.EndPoint,
                RoofAttachedManualOrigin.Split);
        }
        else
        {
            attachedData = new RoofAttachedManualTimberData(
                1,
                ownerHandle,
                attachedManualHandle,
                RoofTimberChildRole.AttachedManual);
        }

        RoofAttachedManualLifecycleService.WriteAnchored(extra, transaction, attachedData);

#if DEBUG
        RoofAttachedManualLifecycleService.WriteAnchorDiag(
            document,
            extra.Handle.ToString(),
            attachedData,
            "ok");
#endif

        WriteSplitOk(
            document,
            globalCommandName,
            ownerHandle,
            generatedHandle,
            attachedManualHandle,
            sourceRole,
            resolvedAnchorKey is null ? null : FormatKey(resolvedAnchorKey.Value));

        _ = RoofAssemblyGroupSyncService.TrySyncForOwnerReference(
            document,
            transaction,
            ownerHandle);
        return true;
    }

    private static bool TryOpenSnapshotLine(
        Database database,
        Transaction transaction,
        string handleText,
        out Line? line)
    {
        line = null;
        if (!long.TryParse(
                handleText,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return false;
        }

        ObjectId objectId;
        try
        {
            objectId = database.GetObjectId(false, new Handle(handleValue), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        return AutoCadObjectIdAccess.TryGetObject<Line>(
            transaction,
            objectId,
            OpenMode.ForRead,
            out line,
            database) &&
            line is not null;
    }

    private static bool TryFinalizeStandaloneFragments(
        Document document,
        Transaction transaction,
        IReadOnlyList<ObjectId> standaloneIds,
        AutoCadTimberElementMetadataStore metadataStore,
        TimberElementDefaultProfile defaultProfile,
        out ManualEditReject? reject)
    {
        reject = null;
        if (standaloneIds.Count == 0)
        {
            return true;
        }

        try
        {
            var synchronized = TimberElementItemIdentityService.SynchronizeElementIds(
                document.Database,
                transaction,
                metadataStore,
                standaloneIds,
                defaultProfile.GetCuttingLengthRoundingStepMm());
            var created = standaloneIds.ToDictionary(
                id => id,
                id => synchronized[id]);
            TimberCreatedElementAnnotationService.EnsureForCreatedElements(
                document.Database,
                transaction,
                created,
                defaultProfile);
            return true;
        }
        catch (System.Exception)
        {
            reject = new ManualEditReject("split", "standalone-init-failure");
            return false;
        }
    }

    private static void TryEraseStandaloneFragments(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> standaloneIds)
    {
        foreach (var id in standaloneIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var fragment,
                    database) ||
                fragment is null ||
                fragment.IsErased)
            {
                continue;
            }

            var sourceHandle = fragment.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceHandle);
            fragment.Erase();
        }
    }

    private static RoofGeneratedMemberGeometry ToSnapshotGeometry(
        RoofUnsupportedStretchTimberLineSnapshotData timber) =>
        new(timber.Start, timber.End);

    private static void WriteSplitOk(
        Document document,
        string? globalCommandName,
        string ownerHandle,
        string generatedFragment,
        string standaloneFragment,
        string sourceRole,
        string? anchor)
    {
#if DEBUG
        if (string.Equals(sourceRole, "AttachedManual", StringComparison.OrdinalIgnoreCase))
        {
            RoofGeneratedMemberManualEditDiag.WriteAttachedManualSplit(
                document.Editor,
                LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
                ownerHandle,
                sourceFragment: generatedFragment,
                newFragment: standaloneFragment,
                anchor: anchor,
                "ok");
        }
        else
        {
            RoofGeneratedMemberManualEditDiag.WriteGeneratedSplit(
                document.Editor,
                LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
                ownerHandle,
                generatedFragment,
                standaloneFragment,
                "ok");
        }
#else
        _ = document;
        _ = globalCommandName;
        _ = ownerHandle;
        _ = generatedFragment;
        _ = standaloneFragment;
        _ = sourceRole;
        _ = anchor;
#endif
    }

    private static bool TryCanonicalGeometry(
        SimpleGableRoofGeometry geometry,
        RoofGeneratedTimberData generated,
        double rafterWidthMm,
        double elevation,
        out RoofGeneratedMemberGeometry canonical)
    {
        canonical = default;
        var layout = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(generated.RequestedMaximumSpacingMm, rafterWidthMm));
        if (!layout.IsValid || layout.Layout is null)
        {
            return false;
        }

        var rafter = layout.Layout.Rafters.FirstOrDefault(item =>
            item.Face == generated.RoofFace && item.StationIndex == generated.StationIndex);
        if (rafter is null)
        {
            return false;
        }

        canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, elevation);
        return true;
    }

    private static void ApplyAcceptedLineGeometry(
        Line line,
        RoofGeneratedMemberGeometry accepted,
        RoofGeneratedMemberGeometry canonical)
    {
        var axisX = canonical.End.X - canonical.Start.X;
        var axisY = canonical.End.Y - canonical.Start.Y;
        var axisZ = canonical.End.Z - canonical.Start.Z;
        var liveX = line.EndPoint.X - line.StartPoint.X;
        var liveY = line.EndPoint.Y - line.StartPoint.Y;
        var liveZ = line.EndPoint.Z - line.StartPoint.Z;
        var reversed = (liveX * axisX) + (liveY * axisY) + (liveZ * axisZ) < 0d;
        var start = reversed ? accepted.End : accepted.Start;
        var end = reversed ? accepted.Start : accepted.End;
        var startPoint = new Point3d(start.X, start.Y, start.Z);
        var endPoint = new Point3d(end.X, end.Y, end.Z);
        if (line.StartPoint.DistanceTo(startPoint) > RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
            line.EndPoint.DistanceTo(endPoint) > RoofGeneratedMemberOverrideMath.LengthToleranceMm)
        {
            line.StartPoint = startPoint;
            line.EndPoint = endPoint;
        }
    }

    private static RoofGeneratedMemberGeometry ToGeometry(Line line) =>
        new(
            new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
            new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));

    private static string FormatKey(RoofGeneratedMemberKey key) =>
        $"{key.MemberKind}:{key.RoofFace}:{key.StationIndex}";

    private static string FormatLine(Line line) => FormatGeometry(ToGeometry(line));

    private static string FormatGeometry(RoofGeneratedMemberGeometry geometry) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "S({0:0.###},{1:0.###},{2:0.###})->E({3:0.###},{4:0.###},{5:0.###})/L={6:0.###}",
            geometry.Start.X,
            geometry.Start.Y,
            geometry.Start.Z,
            geometry.End.X,
            geometry.End.Y,
            geometry.End.Z,
            geometry.LengthMm);

    private static void WriteUnlockedReject(
        Document document,
        string? globalCommandName,
        Polyline owner,
        ManualEditReject? reject)
    {
#if DEBUG
        RoofGeneratedMemberManualEditDiag.Write(
            document.Editor,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
            owner.Handle.ToString(),
            reject?.Handle,
            reject?.Key,
            "Unlocked",
            reject?.Stage ?? "accept",
            reject?.Reason ?? "accept-failed",
            reject?.Before,
            reject?.After);
#else
        _ = document;
        _ = globalCommandName;
        _ = owner;
        _ = reject;
#endif
    }

    private static bool TryResolveErasedMemberKey(
        Database database,
        Transaction transaction,
        string handleText,
        out RoofGeneratedMemberKey key,
        out string? elementId)
    {
        key = default;
        elementId = null;
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
            if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                return false;
            }

            var generated = RoofGeneratedTimberStore.Read(entity);
            if (generated.Data is null)
            {
                return false;
            }

            key = RoofGeneratedMemberKey.From(generated.Data);
            var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
            if (metadataStore.TryRead(entity, out var data) && data is not null)
            {
                elementId = data.ElementId;
            }

            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryClassifyErasedCopyAttachedManual(
        Database database,
        Transaction transaction,
        string handleText,
        out RoofAttachedManualTimberData? data)
    {
        data = null;
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
            if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                return false;
            }

            var stored = RoofAttachedManualTimberStore.Read(entity);
            if (stored.Data is null ||
                (stored.Data.Origin != RoofAttachedManualOrigin.Copy &&
                 stored.Data.Origin != RoofAttachedManualOrigin.Split))
            {
                return false;
            }

            data = stored.Data;
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryIsLiveTimber(Database database, string handleText)
    {
        try
        {
            if (!long.TryParse(
                    handleText,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var handleValue))
            {
                return false;
            }

            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            return !id.IsNull && !id.IsErased;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static void DeleteAnnotationsForHandle(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
        SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
        PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
            database,
            transaction,
            sourceHandle);
    }

    private static bool TryRecoverErasedMembers(
        Document document,
        Transaction transaction,
        ObjectId ownerId)
    {
        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
            return false;
        }

        return RoofUnsupportedStretchRecoveryService.TryUnEraseAndRestore(
            document.Database,
            transaction,
            entry,
            document.Editor);
    }

    private enum OwnerEditOutcome
    {
        Skipped = 0,
        LockedRecovered = 1,
        UnsupportedRecovered = 2,
        Accepted = 3,
        Recovered = 4,
    }

    private sealed class ManualEditReject
    {
        public ManualEditReject(
            string stage,
            string reason,
            string? handle = null,
            string? key = null,
            string? before = null,
            string? after = null)
        {
            Stage = stage;
            Reason = reason;
            Handle = handle;
            Key = key;
            Before = before;
            After = after;
        }

        public string Stage { get; }

        public string Reason { get; }

        public string? Handle { get; }

        public string? Key { get; }

        public string? Before { get; }

        public string? After { get; }
    }
}

internal enum GeneratedMemberEditBatchResult
{
    None = 0,
    Accepted = 1,
    Recovered = 2,
}
