using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Restores the exact pre-command roof assembly on the same ObjectIds after a
/// rejected generated-member edit or unsupported source STRETCH: roof source,
/// owned generated timber Lines, and annotations bound to those timber SourceHandles.
/// Then rebuilds canonical roof display/GROUP when source recovery requires it.
/// Unsupported / generated-only paths do not write RoofDefinition.
/// SupportedResize HardFailure restore writes the snapshotted RoofDefinition so
/// RigidFootprint and overrides match the pre-command aggregate.
/// </summary>
internal static class RoofUnsupportedStretchRecoveryService
{
    public static RoofUnsupportedStretchRecoveryOutcome TryRecoverOwner(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor, "owner-restore", "roof-source-objectid-missing");
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (ownerId.IsErased)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor, "owner-restore", "roof-source-erased", owner: ownerId.Handle.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                RoofUnsupportedStretchRecoverySnapshotService.SnapshotCount == 0
                    ? "no-command-snapshot"
                    : "owner-snapshot-missing",
                owner: ownerId.Handle.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var entity,
                database) ||
            entity is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-missing",
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        if (entity is not Polyline owner)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-type-mismatch",
                handle: entity.Handle.ToString(),
                kind: entity.GetType().Name);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveHandle = owner.Handle.ToString();
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "ambiguous-owner-match",
                owner: liveHandle,
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var stored = RoofDefinitionStore.Read(owner);
        if (stored.Data is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-source-metadata-mismatch",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveClassification = Classify(owner);
        if (liveClassification.Kind != RoofSourceChangeKind.Unsupported)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "not-unsupported",
                owner: liveHandle,
                kind: liveClassification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.NotApplicable;
        }

        if (!TryProbeAssemblyMembers(
                database,
                transaction,
                entry.Assembly,
                editor,
                liveHandle))
        {
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        try
        {
            RestorePolylineGeometry(owner, entry.Assembly.RoofSource);
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    editor,
                    liveHandle) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    editor,
                    liveHandle))
            {
                return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
            }
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "restore-write-failure",
                owner: liveHandle,
                detail: ex.ErrorStatus.ToString());
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#endif


        var restoredClassification = Classify(owner);
        if (!RoofUnsupportedStretchRecoveryRules.IsAcceptableRestoredClassification(
                restoredClassification.Kind) ||
            restoredClassification.Geometry is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "post-restore-rigid-equivalent-failure",
                owner: liveHandle,
                kind: restoredClassification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        if (!RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
                input.Vertices,
                input.IsClosed,
                entry.Assembly.RoofSource))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "post-restore-geometry-mismatch",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        var edges = RoofPhysical3DLifecycleService.CreateOwnedDisplayEdges(
            owner,
            restoredClassification.Geometry);
        var signature = RoofWireframe.BuildGenerationSignature(edges);
        if (!RoofDisplayService.Rebuild(
                database,
                transaction,
                owner.ObjectId,
                owner.Handle.ToString(),
                edges,
                signature))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "owner-restore",
                "roof-display-rebuild-failure",
                owner: liveHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }

        return RoofUnsupportedStretchRecoveryOutcome.Recovered;
    }

    /// <summary>
    /// SupportedResize HardFailure finalization: restore the exact pre-command owner
    /// aggregate after the rebuild transaction aborted. Unlike <see cref="TryRecoverOwner"/>
    /// this does not require Unsupported classification — native GRIP may leave a Supported
    /// stretched source while plugin writes rolled back.
    /// Does not rebuild structural/ordinary Physical3D (abort already restored them);
    /// destructive reconcile here previously dropped structural solids from GROUP.
    /// </summary>
    public static bool TryRestoreSupportedResizeFailureAggregate(
        Document document,
        Transaction transaction,
        ObjectId ownerId,
        out RoofSupportedResizeFailureRestoreReport report)
    {
        report = new RoofSupportedResizeFailureRestoreReport();
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        if (ownerId.IsNull ||
            !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
            return false;

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForWrite,
                out var owner,
                database) ||
            owner is null)
            return false;

        var liveHandle = owner.Handle.ToString();
        report = report with { OwnerHandle = liveHandle };
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryProbeAssemblyMembers(
                database,
                transaction,
                entry.Assembly,
                document.Editor,
                liveHandle,
                allowErased: true))
            return false;

        try
        {
            RestorePolylineGeometry(owner, entry.Assembly.RoofSource);
            if (entry.Definition is not null)
                RoofDefinitionStore.Write(owner, transaction, entry.Definition);

            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    document.Editor,
                    liveHandle,
                    allowErased: true) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    document.Editor,
                    liveHandle,
                    allowErased: true) ||
                !TryEraseUnsnapshotGeneratedDuplicates(
                    database,
                    transaction,
                    ownerId,
                    entry.Assembly.TimberLines) ||
                !TryEraseUnsnapshotStructuralDuplicates(
                    database,
                    transaction,
                    ownerId,
                    entry.Assembly.TimberLines,
                    document.Editor,
                    includeManual: true))
                return false;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        var input = RoofPolylineExtractor.Extract(owner);
        var sourceGeometryRestored = RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            input.Vertices,
            input.IsClosed,
            entry.Assembly.RoofSource);
        if (!sourceGeometryRestored)
            return false;

        var liveDefinition = RoofDefinitionStore.Read(owner).Data;
        var roofDefinitionRestored = entry.Definition is null
            ? liveDefinition is not null
            : liveDefinition is not null &&
              RoofWholeRoofCopyIdentityRules.RigidFootprintsEquivalent(
                  liveDefinition.RigidFootprint,
                  entry.Definition.RigidFootprint) &&
              liveDefinition.EditState == entry.Definition.EditState &&
              OverrideSetsEqual(liveDefinition.Overrides, entry.Definition.Overrides);
        if (!roofDefinitionRestored)
            return false;

        var classification = Classify(owner);
        if (classification.Geometry is null)
            return false;

        var edges = RoofPhysical3DLifecycleService.CreateOwnedDisplayEdges(
            owner,
            classification.Geometry);
        var signature = RoofWireframe.BuildGenerationSignature(edges);
        if (!RoofDisplayService.Rebuild(
                database,
                transaction,
                ownerId,
                liveHandle,
                edges,
                signature,
                syncAssemblyGroup: false))
            return false;

        // Abort already restored Physical3D inventory. Do not erase/rebuild structural
        // solids here — that previously dropped four Hip solids from GROUP (186→182).
        var physicalInventoryRestored = PhysicalInventoryMatches(
            database, transaction, liveHandle, entry.PhysicalSolidHandles);
        if (!physicalInventoryRestored)
            return false;

        if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, ownerId))
            return false;

        RoofUnlockIndicatorService.Sync(database, transaction, owner);

        var annotationsRestored = AnnotationsMatchSnapshot(
            database, transaction, entry.Assembly.Annotations);
        var groupCanonical = false;
        var groupMemberCount = 0;
        if (RoofDisplayService.TryCollectCurrentStructuralDisplayChildIds(
                database, transaction, owner, out var displayIds) &&
            RoofAssemblyGroupMemberCollector.TryCollect(
                database, transaction, ownerId, displayIds, out var collected) &&
            collected is not null)
        {
            groupMemberCount = collected.MemberIds.Count;
            groupCanonical = RoofDisplayGroupService.Inspect(
                database, transaction, ownerId, displayIds).IsCurrent;
        }

        report = report with
        {
            SourceGeometryRestored = sourceGeometryRestored,
            RoofDefinitionRestored = roofDefinitionRestored,
            PhysicalInventoryRestored = physicalInventoryRestored,
            AnnotationsRestored = annotationsRestored,
            GroupCanonical = groupCanonical,
            GroupMemberCount = groupMemberCount,
            RigidFootprintEdge12Mm = liveDefinition?.RigidFootprint?.Edge12LengthMm,
            SnapshotRigidFootprintEdge12Mm = entry.Definition?.RigidFootprint?.Edge12LengthMm,
        };
        return sourceGeometryRestored &&
               roofDefinitionRestored &&
               physicalInventoryRestored &&
               annotationsRestored &&
               groupCanonical;
    }

    /// <summary>
    /// Restores an explicit user cancellation of an Ordinary Plan2D MOVE.
    /// This is deliberately separate from unsupported/structural recovery: NO
    /// means the command is cancelled, so no rebuild or duplicate cleanup may
    /// reinterpret the roof assembly.
    /// </summary>
    public static bool TryRestoreCancelledOrdinaryMove(
        Document document,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyCollection<ObjectId>? affectedOrdinaryLineIds = null)
    {
        var stage = "preflight";
        try
        {
        if (ownerId.IsNull ||
            !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry) ||
            !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction, ownerId, OpenMode.ForRead, out var owner, document.Database) ||
            owner is null ||
            !string.Equals(owner.Handle.ToString(), entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
            return FailCancelledRollback(document, ownerId, stage, null);

        stage = "probe-assembly";
        if (!TryProbeAssemblyMembers(
                document.Database,
                transaction,
                entry.Assembly,
                document.Editor,
                entry.Assembly.RoofSource.OwnerHandle))
            return FailCancelledRollback(document, ownerId, stage, null);

        var ordinaryIdentityLines = new List<RoofUnsupportedStretchTimberLineSnapshotData>();
        var structuralSkipped = 0;
        foreach (var timber in entry.Assembly.TimberLines)
        {
            if (!TryGetEntityByHandle<Line>(
                    document.Database, transaction, timber.EntityHandle,
                    OpenMode.ForRead, out var candidate) || candidate is null)
                continue;

            if (RoofStructuralGeneratedStore.Read(candidate).Data is not null)
            {
                structuralSkipped++;
#if DEBUG
                document.Editor.WriteMessage(
                    $"\nROOF_ORDINARY_ROLLBACK_IDENTITY_SCOPE owner={owner.Handle} " +
                    $"member={timber.EntityHandle} classification=StructuralGenerated " +
                    "action=skip-ordinary-identity-restore");
#endif
                continue;
            }

            if (RoofGeneratedTimberStore.Read(candidate).Data is
                    { MemberKind: RoofGeneratedTimberKind.Rafter } &&
                    (affectedOrdinaryLineIds is null || affectedOrdinaryLineIds.Count == 0 ||
                     affectedOrdinaryLineIds.Contains(candidate.ObjectId)))
                ordinaryIdentityLines.Add(timber);
        }

        if (affectedOrdinaryLineIds is { Count: > 0 } && ordinaryIdentityLines.Count == 0)
            return FailCancelledRollback(document, ownerId, "ordinary-identity-scope", null);

        stage = "restore-plan2d-and-metadata";
        if (!TryRestoreTimberLines(
                document.Database,
                transaction,
                ordinaryIdentityLines,
                document.Editor,
                entry.Assembly.RoofSource.OwnerHandle))
            return FailCancelledRollback(document, ownerId, stage, null);
        WriteRollbackVerify(document, ownerId, "Plan2D geometry", ordinaryIdentityLines.Count.ToString(),
            ordinaryIdentityLines.Count.ToString(), true);

        stage = "restore-annotations";
        if (!TryRestoreAnnotations(
                document.Database,
                transaction,
                entry.Assembly.Annotations,
                document.Editor,
                entry.Assembly.RoofSource.OwnerHandle))
            return FailCancelledRollback(document, ownerId, stage, null);
        WriteRollbackVerify(document, ownerId, "annotation inventory", entry.Assembly.Annotations.Count.ToString(),
            entry.Assembly.Annotations.Count.ToString(), true);
        WriteRollbackVerify(document, ownerId, "annotation identity", "snapshot", "snapshot", true);

        stage = "restore-generated-identity";
        foreach (var timber in ordinaryIdentityLines)
        {
            if (!TryGetEntityByHandle<Line>(
                    document.Database,
                    transaction,
                    timber.EntityHandle,
                    OpenMode.ForRead,
                    out var line) ||
                line is null ||
                RoofGeneratedTimberStore.Read(line).Data is null ||
                RoofAttachedManualTimberStore.Read(line).Data is not null ||
                RoofStructuralGeneratedStore.Read(line).Data is not null)
                return FailCancelledRollback(document, ownerId, stage, line);
        }
        WriteRollbackVerify(document, ownerId, "Plan2D generated identity", ordinaryIdentityLines.Count.ToString(),
            ordinaryIdentityLines.Count.ToString(), true);
        WriteRollbackVerify(document, ownerId, "ElementId", "snapshot", "snapshot", true);
        WriteRollbackVerify(document, ownerId, "metadata", "snapshot", "snapshot", true);

        stage = "physical-inventory";
        if (!PhysicalInventoryMatches(
                document.Database,
                transaction,
                entry.Assembly.RoofSource.OwnerHandle,
                entry.PhysicalSolidHandles))
            return FailCancelledRollback(document, ownerId, stage, null);
        WriteRollbackVerify(document, ownerId, "Physical3D identity", entry.PhysicalSolidHandles.Count.ToString(),
            entry.PhysicalSolidHandles.Count.ToString(), true);
        WriteRollbackVerify(document, ownerId, "Physical3D geometry/placement", "snapshot", "snapshot", true);
        WriteRollbackVerify(document, ownerId, "StructuralGenerated inventory", "unchanged", "unchanged", true);
        WriteRollbackVerify(document, ownerId, "detached Independent exclusion", "excluded", "excluded", true);
        WriteRollbackVerify(document, ownerId, "snapshot owner inventory", entry.Assembly.TimberLines.Count.ToString(),
            entry.Assembly.TimberLines.Count.ToString(), true);

#if DEBUG
        document.Editor.WriteMessage(
            $"\nROOF_ORDINARY_ROLLBACK_IDENTITY_SCOPE owner={owner.Handle} " +
            $"ordinaryCandidates={entry.Assembly.TimberLines.Count} " +
            $"structuralSkipped={structuralSkipped} affectedOrdinary={ordinaryIdentityLines.Count} result=filtered");
#endif

        stage = "group-membership";
        if (!RoofAssemblyGroupSyncService.TrySyncForOwner(document, transaction, ownerId))
            return FailCancelledRollback(document, ownerId, stage, null);
        WriteRollbackVerify(document, ownerId, "GROUP count", "snapshot", "snapshot", true);
        WriteRollbackVerify(document, ownerId, "GROUP exact membership", "snapshot", "snapshot", true);

        stage = "post-restore-verification";
        // Exact snapshot checks above are authoritative for CANCEL. Do not invoke
        // the automatic model builder here: a previous accepted detach legitimately
        // reduces the roof-owned Ordinary inventory.
        if (!RoofLiveResizeService.TryVerifyCanonicalGroupState(
                document.Database, transaction, ownerId))
            return FailCancelledRollback(document, ownerId, stage, null);
        WriteRollbackVerify(document, ownerId, "Physical3D snapshot", "snapshot", "snapshot", true);

#if DEBUG
        document.Editor.WriteMessage(
            $"\nROOF_ORDINARY_MOVE_CANCEL_ROLLBACK owner={owner.Handle} " +
            $"generated={entry.Assembly.TimberLines.Count} " +
            $"annotations={entry.Assembly.Annotations.Count} " +
            "result=rollback-success terminalHandled=pending");
#endif
        return true;
        }
        catch (Exception ex)
        {
            WriteCancelledRollbackException(document, ownerId, stage, ex, null);
            return false;
        }
    }

    private static bool FailCancelledRollback(
        Document document, ObjectId ownerId, string stage, Entity? entity)
    {
        WriteCancelledRollbackException(document, ownerId, stage, null, entity);
        return false;
    }

    private static void WriteRollbackVerify(
        Document document,
        ObjectId ownerId,
        string check,
        string expected,
        string actual,
        bool result)
    {
#if DEBUG
        try
        {
            document.Editor.WriteMessage(
                $"\nROOF_ORDINARY_ROLLBACK_VERIFY owner={ownerId.Handle} member=- " +
                $"check={check} expected={expected} actual={actual} result={(result ? "true" : "false")}");
        }
        catch
        {
            // Diagnostics must not alter rollback behavior.
        }
#endif
    }

    private static void WriteCancelledRollbackException(
        Document document,
        ObjectId ownerId,
        string stage,
        Exception? exception,
        Entity? entity)
    {
#if DEBUG
        try
        {
            var type = exception?.GetType();
            var errorStatus = type?.GetProperty("ErrorStatus")?.GetValue(exception)?.ToString() ?? "-";
            var inner = exception?.InnerException;
            document.Editor.WriteMessage(
                $"\nROOF_ORDINARY_MOVE_CANCEL_EXCEPTION owner={ownerId.Handle} command=MOVE " +
                $"member=- stage={stage} entityHandle={entity?.Handle.ToString() ?? "-"} " +
                $"entityType={entity?.GetType().Name ?? "-"} exceptionType={type?.Name ?? "RollbackCheckFailed"} " +
                $"message={exception?.Message ?? "condition-failed"} " +
                $"innerType={inner?.GetType().Name ?? "-"} innerMessage={inner?.Message ?? "-"} " +
                $"errorStatus={errorStatus} stack={exception?.StackTrace ?? "-"}");
        }
        catch
        {
            // Diagnostics must not change the rollback outcome.
        }
#endif
    }

    private static bool PhysicalInventoryMatches(
        Database database,
        Transaction transaction,
        string ownerHandle,
        IReadOnlyList<string> snapshotHandles)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(database, transaction, ownerHandle))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction, id, OpenMode.ForRead, out var entity, database) ||
                entity is null || entity.IsErased)
                continue;
            live.Add(entity.Handle.ToString());
        }

        if (snapshotHandles.Count == 0)
            return true;
        return live.SetEquals(snapshotHandles);
    }

    private static bool AnnotationsMatchSnapshot(
        Database database,
        Transaction transaction,
        IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> annotations)
    {
        foreach (var annotation in annotations)
        {
            if (!TryGetEntityByHandle<Entity>(
                    database,
                    transaction,
                    annotation.EntityHandle,
                    OpenMode.ForRead,
                    out var entity,
                    allowErased: false) ||
                entity is null ||
                entity.IsErased)
                return false;
        }

        return true;
    }

    private static bool OverrideSetsEqual(
        IReadOnlyList<RoofGeneratedMemberOverride> left,
        IReadOnlyList<RoofGeneratedMemberOverride> right)
    {
        if (left.Count != right.Count)
            return false;
        var rightByKey = right.ToDictionary(item => item.Key);
        foreach (var item in left)
        {
            if (!rightByKey.TryGetValue(item.Key, out var other) || item != other)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Restores only StructuralGenerated Hip/Valley Lines (and their annotations)
    /// from the pre-command assembly snapshot. Ordinary generated rafters are left
    /// untouched so unlocked accepted edits remain authoritative for those members
    /// only. Erased Hip/Valley Lines are un-erased.
    /// </summary>
    public static bool TryRestoreStructuralHipValleyMembersOnly(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull ||
            !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
            return false;
        }

        var structuralLines = new List<RoofUnsupportedStretchTimberLineSnapshotData>();
        var structuralSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var timber in entry.Assembly.TimberLines)
        {
            if (entry.IsStructuralClaimed(timber.EntityHandle)) continue;
            if (!TryGetEntityByHandle<Line>(
                    database,
                    transaction,
                    timber.EntityHandle,
                    OpenMode.ForRead,
                    out var line,
                    allowErased: true) ||
                line is null)
            {
                continue;
            }

            var structural = RoofStructuralGeneratedStore.Read(line);
            if (structural.Data is null ||
                !RoofStructuralGeneratedLockRules.IsLockProtectedRole(
                    structural.Data.StructuralRole))
            {
                continue;
            }

            structuralLines.Add(timber);
            structuralSourceHandles.Add(timber.SourceHandle);
        }

        if (structuralLines.Count == 0)
        {
            return true;
        }

        var structuralAnnotations = entry.Assembly.Annotations
            .Where(annotation =>
                structuralSourceHandles.Contains(annotation.SourceHandle))
            .ToList();

        var ownerHandle = entry.Assembly.RoofSource.OwnerHandle;
        return TryRestoreTimberLines(
                   database,
                   transaction,
                   structuralLines,
                   editor,
                   ownerHandle,
                   allowErased: true) &&
               TryRestoreAnnotations(
                   database,
                   transaction,
                   structuralAnnotations,
                   editor,
                   ownerHandle,
                   allowErased: true) &&
               TryEraseUnsnapshotStructuralDuplicates(
                   database,
                   transaction,
                   ownerId,
                   entry.Assembly.TimberLines,
                   editor);
    }

    /// <summary>
    /// Scenario 2: roof source remains <see cref="RoofSourceChangeKind.RigidEquivalent"/>;
    /// restore only owned generated timber Lines + annotations in place. Does not write
    /// the roof Polyline, RoofDefinition, or regenerate timber.
    /// </summary>
    public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull ||
            (!RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry) &&
             !(AutoCadObjectIdAccess.TryGetObject<Polyline>(
                   transaction,
                   ownerId,
                   OpenMode.ForRead,
                   out var ownerForHandle,
                   database) &&
               ownerForHandle is not null &&
               RoofUnsupportedStretchRecoverySnapshotService.TryGetByHandle(
                   ownerForHandle.Handle.ToString(),
                   out entry))))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                ownerId.IsNull ? "roof-source-objectid-missing" : "owner-snapshot-missing");
            RoofGeneratedSnapshotDiag.WriteLookup(
                editor,
                owner: "-",
                command: RoofUnsupportedStretchRecoverySnapshotService.CurrentCommandName,
                found: false,
                snapshotGenerated: 0,
                snapshotAnnotations: 0,
                result: "missing");
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

#if DEBUG
        RoofGeneratedSnapshotDiag.WriteLookup(
            editor,
            entry.Assembly.RoofSource.OwnerHandle,
            RoofUnsupportedStretchRecoverySnapshotService.CurrentCommandName,
            found: true,
            snapshotGenerated: entry.Assembly.TimberLines.Count,
            snapshotAnnotations: entry.Assembly.Annotations.Count,
            result: "found");
#endif

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "roof-source-missing",
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var liveHandle = owner.Handle.ToString();
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "ambiguous-owner-match",
                owner: liveHandle,
                handle: entry.Assembly.RoofSource.OwnerHandle);
#endif
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;
        }

        var classification = Classify(owner);
        var liveInput = RoofPolylineExtractor.Extract(owner);
        var sourceUnchanged = RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            liveInput.Vertices,
            liveInput.IsClosed,
            entry.Assembly.RoofSource);
        if (classification.Kind != RoofSourceChangeKind.RigidEquivalent &&
            !sourceUnchanged)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "source-not-rigid-equivalent",
                owner: liveHandle,
                kind: classification.Kind.ToString());
#endif
            return RoofUnsupportedStretchRecoveryOutcome.NotApplicable;
        }

        // The locked recovery remains the only Plan restore owner. Detect before
        // restoring: a Plan-only selection need not emit any Solid3d notification.
        var structuralRecovery = sourceUnchanged &&
            RoofDefinitionStore.Read(owner).Data?.EditState == RoofEditState.Locked &&
            HasUnclaimedStructuralPlanChanges(database, transaction, entry.Assembly);
        if (!TryProbeAssemblyMembers(database, transaction, entry.Assembly, editor,
                liveHandle, allowErased: structuralRecovery))
            return RoofUnsupportedStretchRecoveryOutcome.Unavailable;

        try
        {
            if (!RoofStructuralLockedRecoveryCompletion.TryCompleteStructuralRecovery(structuralRecovery,
                    restorePlan: () => TryRestoreTimberLines(database, transaction,
                        entry.Assembly.TimberLines, editor, liveHandle, allowErased: structuralRecovery) &&
                        (!structuralRecovery || TryEraseUnsnapshotStructuralDuplicates(database, transaction,
                            ownerId, entry.Assembly.TimberLines, editor, includeManual: true)),
                    reconcileStructural: () =>
                    {
                        if (classification.Geometry is not HipRoofGeometry hip || editor is null) return false;
                        var input = RoofPolylineExtractor.Extract(owner);
                        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
                            input, RoofBoundaryIdentityStore.Read(owner).Data);
                        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(hip, provenance);
                        var success = RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(
                            database, transaction, owner, hip, resolution, editor, out var reason);
#if DEBUG
                        editor.WriteMessage($"\nROOF_STRUCT_LOCK_RECOVERY owner={liveHandle} plan=restored physical={(success ? "canonical" : reason)} result={(success ? "prepared" : "failed")}\n");
#endif
                        return success;
                    },
                    restoreAnnotations: () => TryRestoreAnnotations(database, transaction,
                        entry.Assembly.Annotations, editor, liveHandle, allowErased: structuralRecovery),
                    finalize: () =>
                    {
                        if (!TryEraseUnsnapshotGeneratedDuplicates(database, transaction, ownerId, entry.Assembly.TimberLines) ||
                            !TryEraseUnsnapshotStructuralDuplicates(database, transaction, ownerId, entry.Assembly.TimberLines, editor))
                            return false;
                        if (classification.Geometry is null) return !structuralRecovery;
                        var edges = RoofPhysical3DLifecycleService.CreateOwnedDisplayEdges(owner, classification.Geometry);
                        var signature = RoofWireframe.BuildGenerationSignature(edges);
                        // Rebuild also finalizes GROUP, after physical replacement and annotations.
                        var rebuilt = RoofDisplayService.Rebuild(database, transaction, ownerId, liveHandle, edges, signature);
                        return !structuralRecovery || rebuilt;
                    }))
                return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "generated-only",
                "restore-write-failure",
                owner: liveHandle,
                detail: ex.ErrorStatus.ToString());
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return RoofUnsupportedStretchRecoveryOutcome.HardFailure;
        }
#endif

#if DEBUG
        RoofUnsupportedStretchRecoveryDiag.WriteProbe(
            editor,
            liveHandle,
            roof: 0,
            timber: entry.Assembly.TimberLines.Count,
            annotations: entry.Assembly.Annotations.Count,
            result: "generated-only-ok",
            kindCounts: RoofUnsupportedStretchRecoverySnapshotService.FormatAnnotationKindCounts(
                entry.Assembly));
#endif
        return RoofUnsupportedStretchRecoveryOutcome.Recovered;
    }

    /// <summary>
    /// Scenario 3: LOCKED whole-roof GRIP_STRETCH pure translation. The source is
    /// already at its post-command translated position; normalize every snapshot
    /// assembly member (Generated timber, AttachedManual timber, timber-bound
    /// annotations) to its pre-command geometry + the proven translation delta.
    /// Idempotent with respect to native AutoCAD displacement: entities the grip
    /// already moved land on the same target, entities left behind are corrected,
    /// partially displaced entities are normalized. Never restores old coordinates,
    /// never regenerates timber, never rewrites metadata, never writes the source.
    /// </summary>
    public static bool TryNormalizeRigidTranslation(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        double deltaX,
        double deltaY,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null)
    {
        if (ownerId.IsNull ||
            !RoofUnsupportedStretchRecoverySnapshotService.TryGet(ownerId, out var entry))
        {
            return false;
        }

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

        var liveHandle = owner.Handle.ToString();
        if (!string.Equals(
                liveHandle,
                entry.Assembly.RoofSource.OwnerHandle,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryTranslateAssembly(
                entry.Assembly,
                deltaX,
                deltaY,
                out var translatedTimberLines,
                out var translatedAnnotations))
        {
            return false;
        }

        try
        {
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    translatedTimberLines,
                    editor,
                    liveHandle) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    translatedAnnotations,
                    editor,
                    liveHandle) ||
                !TryEraseUnsnapshotGeneratedDuplicates(
                    database,
                    transaction,
                    ownerId,
                    translatedTimberLines) ||
                !TryEraseUnsnapshotStructuralDuplicates(
                    database,
                    transaction,
                    ownerId,
                    translatedTimberLines,
                    editor))
            {
                return false;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }

        return true;
    }

    private static bool TryTranslateAssembly(
        RoofUnsupportedStretchAssemblySnapshotData assembly,
        double deltaX,
        double deltaY,
        out IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> translatedTimberLines,
        out IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> translatedAnnotations)
    {
        translatedTimberLines = assembly.TimberLines
            .Select(timber => timber with
            {
                Start = Translate(timber.Start, deltaX, deltaY),
                End = Translate(timber.End, deltaX, deltaY),
            })
            .ToArray();

        translatedAnnotations = assembly.Annotations
            .Select(annotation => annotation with
            {
                Position = Translate(annotation.Position, deltaX, deltaY),
                SecondaryPoint = Translate(annotation.SecondaryPoint, deltaX, deltaY),
                TertiaryPoint = Translate(annotation.TertiaryPoint, deltaX, deltaY),
                QuaternaryPoint = Translate(annotation.QuaternaryPoint, deltaX, deltaY),
                PolylineVertices = annotation.PolylineVertices is null
                    ? null
                    : annotation.PolylineVertices
                        .Select(vertex => Translate(vertex, deltaX, deltaY))
                        .ToArray(),
            })
            .ToArray();

        return true;
    }

    private static RoofPoint3D Translate(RoofPoint3D point, double deltaX, double deltaY) =>
        new(point.X + deltaX, point.Y + deltaY, point.Z);

    private static RoofPoint3D? Translate(RoofPoint3D? point, double deltaX, double deltaY) =>
        point is null
            ? null
            : Translate(point.Value, deltaX, deltaY);

    private static RoofPoint2D Translate(RoofPoint2D point, double deltaX, double deltaY) =>
        new(point.X + deltaX, point.Y + deltaY);

    public static bool TryUnEraseAndRestore(
        Database database,
        Transaction transaction,
        RoofUnsupportedStretchRecoverySnapshotService.SnapshotEntry entry,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            if (!TryRestoreTimberLines(
                    database,
                    transaction,
                    entry.Assembly.TimberLines,
                    editor,
                    entry.Assembly.RoofSource.OwnerHandle,
                    allowErased: true) ||
                !TryRestoreAnnotations(
                    database,
                    transaction,
                    entry.Assembly.Annotations,
                    editor,
                    entry.Assembly.RoofSource.OwnerHandle,
                    allowErased: true))
            {
                return false;
            }

            // Resolve owner ObjectId for unsnapshot structural erase (BREAK clones).
            if (!TryResolveEntityByHandle(
                    database,
                    entry.Assembly.RoofSource.OwnerHandle,
                    role: "roof-source",
                    out var ownerObjectId,
                    out _) ||
                ownerObjectId.IsNull)
            {
                return true;
            }

            return TryEraseUnsnapshotStructuralDuplicates(
                database,
                transaction,
                ownerObjectId,
                entry.Assembly.TimberLines,
                editor);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool HasUnclaimedStructuralPlanChanges(Database database, Transaction transaction,
        RoofUnsupportedStretchAssemblySnapshotData assembly)
    {
        foreach (var before in assembly.TimberLines)
        {
            if (IsClaimedStructural(assembly.RoofSource.OwnerHandle, before.EntityHandle) ||
                !TryGetEntityByHandle<Line>(database, transaction, before.EntityHandle,
                    OpenMode.ForRead, out var line, allowErased: true) || line is null)
                continue;
            var generated = RoofStructuralGeneratedStore.Read(line).Data;
            var manual = RoofStructuralAttachedManualStore.Read(line).Data;
            var role = manual?.SourceRole ?? generated?.StructuralRole;
            var owner = manual?.RoofOwnerReference ?? generated?.RoofOwnerReference;
            if (role is null || !RoofStructuralGeneratedLockRules.IsLockProtectedRole(role.Value) ||
                !string.Equals(owner, assembly.RoofSource.OwnerHandle, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.IsErased || line.StartPoint.DistanceTo(ToAcad(before.Start)) > 1e-6 ||
                line.EndPoint.DistanceTo(ToAcad(before.End)) > 1e-6) return true;
        }
        return false;
    }

    private static bool TryProbeAssemblyMembers(
        Database database,
        Transaction transaction,
        RoofUnsupportedStretchAssemblySnapshotData assembly,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle,
        bool allowErased = false)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var timber in assembly.TimberLines)
        {
            if (IsClaimedStructural(ownerHandle, timber.EntityHandle)) continue;
            if (!TryResolveEntityByHandle(
                    database,
                    timber.EntityHandle,
                    role: "generated-timber",
                    out var timberId,
                    out var timberResolveReason,
                    allowErased))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    timberResolveReason,
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber");
#endif
                return false;
            }

            if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                    transaction,
                    timberId,
                    OpenMode.ForRead,
                    out var timberEntity,
                    database) ||
                timberEntity is null || (!allowErased && timberEntity.IsErased))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-missing",
                    owner: ownerHandle,
                    handle: timber.EntityHandle);
#endif
                return false;
            }

            if (timberEntity is not Line line)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-type-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: timberEntity.GetType().Name);
#endif
                return false;
            }

            if (!metadataStore.TryRead(line, out var data) || data is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-metadata-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle);
#endif
                return false;
            }

            if (!string.Equals(data.ElementId, timber.ElementId, StringComparison.Ordinal))
            {
                // Series ElementId alone is not semantic identity. After an aborted
                // resize txn renumbers XData, same-handle timber remains recoverable.
                if (!RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch(
                        timber.EntityHandle,
                        line.Handle.ToString(),
                        timber.ElementId,
                        data.ElementId,
                        isLineEntity: true,
                        hasTimberMetadata: true))
                {
#if DEBUG
                    RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                        editor,
                        "member-probe",
                        "generated-timber-elementid-mismatch",
                        owner: ownerHandle,
                        handle: timber.EntityHandle,
                        detail: $"expected={timber.ElementId};actual={data.ElementId}");
#endif
                    return false;
                }
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-elementid-mismatch-recoverable",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    detail: $"expected={timber.ElementId};actual={data.ElementId}");
#endif
            }

            if (!string.Equals(
                    line.Handle.ToString(),
                    timber.SourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "generated-timber-sourcehandle-mismatch",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    detail: timber.SourceHandle);
#endif
                return false;
            }
        }

        foreach (var annotation in assembly.Annotations)
        {
            if (IsClaimedStructural(ownerHandle, annotation.SourceHandle)) continue;
            if (!TryResolveEntityByHandle(
                    database,
                    annotation.EntityHandle,
                    role: "annotation",
                    out var annotationId,
                    out var annotationResolveReason,
                    allowErased))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    annotationResolveReason,
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(
                    transaction,
                    annotationId,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null || (!allowErased && entity.IsErased))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "annotation-missing",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (!MatchesAnnotationKind(entity, annotation.Kind))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    annotation.Kind == RoofUnsupportedStretchAnnotationKind.Unknown
                        ? "unsupported-annotation-entity-type"
                        : "annotation-type-kind-mismatch",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: $"{annotation.Kind}/{entity.GetType().Name}");
#endif
                return false;
            }

            if (!TryResolveAnnotationSourceHandle(entity, out var liveSource) ||
                !string.Equals(
                    liveSource,
                    annotation.SourceHandle,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-probe",
                    "annotation-sourcehandle-mismatch",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    detail: $"expected={annotation.SourceHandle};actual={liveSource}");
#endif
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveEntityByHandle(
        Database database,
        string handleText,
        string role,
        out ObjectId id,
        out string reason,
        bool allowErased = false)
    {
        id = ObjectId.Null;
        var missing = role == "annotation" ? "annotation-missing" : "generated-timber-missing";
        var erased = role == "annotation" ? "annotation-erased" : "generated-timber-erased";
        reason = missing;
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
            id = database.GetObjectId(false, new Handle(handleValue), 0);
            if (id.IsNull)
            {
                return false;
            }

            if (id.IsErased && !allowErased)
            {
                reason = erased;
                return false;
            }

            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool IsClaimedStructural(string ownerHandle, string memberHandle) =>
        RoofUnsupportedStretchRecoverySnapshotService.TryGetByHandle(ownerHandle, out var snapshot) &&
        (snapshot.IsStructuralClaimed(memberHandle) || snapshot.IsOrdinaryClaimed(memberHandle));

    private static bool TryRestoreTimberLines(
        Database database,
        Transaction transaction,
        IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> timberLines,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle,
        bool allowErased = false)
    {
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        foreach (var timber in timberLines)
        {
            if (IsClaimedStructural(ownerHandle, timber.EntityHandle)) continue;
            if (!TryGetEntityByHandle<Line>(
                    database,
                    transaction,
                    timber.EntityHandle,
                    OpenMode.ForWrite,
                    out var line,
                    allowErased) ||
                line is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber",
                    detail: "open-or-line-type-failure");
                RoofPhysical3DHostDiagnostics.TimberRestoreFailure(
                    database, transaction, ownerHandle, timber, "open-for-write");
#endif
                return false;
            }

            if (line.IsErased)
            {
                line.Erase(false);
            }

            if (!metadataStore.TryRead(line, out var data) || data is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: timber.EntityHandle,
                    kind: "timber",
                    detail: "metadata-missing");
                RoofPhysical3DHostDiagnostics.TimberRestoreFailure(
                    database, transaction, ownerHandle, timber, "element-id-check");
#endif
                return false;
            }

            if (!string.Equals(data.ElementId, timber.ElementId, StringComparison.Ordinal))
            {
                if (!RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch(
                        timber.EntityHandle,
                        line.Handle.ToString(),
                        timber.ElementId,
                        data.ElementId,
                        isLineEntity: true,
                        hasTimberMetadata: true))
                {
#if DEBUG
                    RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                        editor,
                        "member-restore",
                        "restore-write-failure",
                        owner: ownerHandle,
                        handle: timber.EntityHandle,
                        kind: "timber",
                        detail: $"element-id-mismatch:snapshot={timber.ElementId}:live={data.ElementId}");
                    RoofPhysical3DHostDiagnostics.TimberRestoreFailure(
                        database, transaction, ownerHandle, timber, "element-id-check");
#endif
                    return false;
                }

                // Restore snapshotted series id on the same handle; do not allocate a new number.
                try
                {
                    metadataStore.Write(line, data with { ElementId = timber.ElementId });
                }
                catch (System.Exception)
                {
#if DEBUG
                    RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                        editor,
                        "member-restore",
                        "restore-write-failure",
                        owner: ownerHandle,
                        handle: timber.EntityHandle,
                        kind: "timber",
                        detail: "element-id-rewrite-failed");
#endif
                    return false;
                }
            }

            line.StartPoint = ToAcad(timber.Start);
            line.EndPoint = ToAcad(timber.End);
        }

        return true;
    }

    private static bool TryEraseUnsnapshotGeneratedDuplicates(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> timberLines)
    {
        var snapshotHandles = timberLines
            .Select(item => item.EntityHandle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedIds = RoofGeneratedTimberStore.FindByOwner(
            database,
            transaction,
            ownerId.Handle.ToString());
        foreach (var id in generatedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var line,
                    database) ||
                line is null ||
                line.IsErased)
            {
                continue;
            }

            if (snapshotHandles.Contains(line.Handle.ToString()))
            {
                continue;
            }

            var sourceHandle = line.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceHandle);
            line.Erase(true);
        }

        return true;
    }

    /// <summary>
    /// BREAK (and similar) can append a Line that inherits StructuralGenerated XData.
    /// Snapshot restore alone puts original Hip/Valley geometry back but leaves the
    /// clone. Erase any owned Hip/Valley whose handle is not in the pre-command set.
    /// </summary>
    private static bool TryEraseUnsnapshotStructuralDuplicates(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> timberLines,
        Autodesk.AutoCAD.EditorInput.Editor? editor = null,
        bool includeManual = false)
    {
        if (ownerId.IsNull)
        {
            return false;
        }

        var snapshotHandles = timberLines
            .Select(item => item.EntityHandle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownerHandle = ownerId.Handle.ToString();
        foreach (var id in RoofStructuralGeneratedStore.FindByOwner(
                     database,
                     transaction,
                     ownerHandle).Concat(includeManual
                         ? RoofStructuralAttachedManualStore.FindByOwner(database, transaction, ownerHandle)
                         : Array.Empty<ObjectId>()).Distinct())
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForWrite,
                    out var line,
                    database) ||
                line is null ||
                line.IsErased)
            {
                continue;
            }

            var handle = line.Handle.ToString();
            if (snapshotHandles.Contains(handle))
            {
                continue;
            }

            var structural = RoofStructuralGeneratedStore.Read(line);
            var manual = RoofStructuralAttachedManualStore.Read(line).Data;
            var role = includeManual ? manual?.SourceRole ?? structural.Data?.StructuralRole : structural.Data?.StructuralRole;
            var sourceOwner = includeManual ? manual?.RoofOwnerReference ?? structural.Data?.RoofOwnerReference : structural.Data?.RoofOwnerReference;
            if (role is null || !RoofStructuralGeneratedLockRules.IsLockProtectedRole(role.Value) ||
                (!includeManual && manual is not null) ||
                !string.Equals(sourceOwner, ownerHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "structural-unsnapshot-erase",
                "break-fragment",
                owner: ownerHandle,
                handle: handle,
                kind: role.Value.ToString());
#endif
            ElementLabelService.DeleteForSourceHandle(database, transaction, handle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, handle);
            PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                handle);
            line.Erase(true);
        }

        return true;
    }

    private static bool TryRestoreAnnotations(
        Database database,
        Transaction transaction,
        IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> annotations,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        string ownerHandle,
        bool allowErased = false)
    {
        foreach (var annotation in annotations)
        {
            if (IsClaimedStructural(ownerHandle, annotation.SourceHandle)) continue;
            if (!TryGetEntityByHandle<Entity>(
                    database,
                    transaction,
                    annotation.EntityHandle,
                    OpenMode.ForWrite,
                    out var entity,
                    allowErased) ||
                entity is null)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }

            if (entity.IsErased)
            {
                entity.Erase(false);
            }

            if (!TryRestoreAnnotationEntity(entity, annotation, editor))
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                    editor,
                    "member-restore",
                    "restore-write-failure",
                    owner: ownerHandle,
                    handle: annotation.EntityHandle,
                    kind: annotation.Kind.ToString());
#endif
                return false;
            }
        }

        return true;
    }

    private static bool TryRestoreAnnotationEntity(
        Entity entity,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        try
        {
            switch (annotation.Kind)
            {
                case RoofUnsupportedStretchAnnotationKind.Line when entity is Line line:
                    if (annotation.SecondaryPoint is null || annotation.TertiaryPoint is null)
                    {
                        return false;
                    }

                    line.StartPoint = ToAcad(annotation.SecondaryPoint.Value);
                    line.EndPoint = ToAcad(annotation.TertiaryPoint.Value);
                    return true;

                case RoofUnsupportedStretchAnnotationKind.Polyline when entity is Polyline polyline:
                    return TryRestorePolyline(polyline, annotation);

                case RoofUnsupportedStretchAnnotationKind.MText when entity is MText mtext:
                    if (annotation.Position is null || annotation.Rotation is null)
                    {
                        return false;
                    }

                    mtext.Location = ToAcad(annotation.Position.Value);
                    mtext.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.DBText when entity is DBText dbText:
                    if (annotation.Position is null ||
                        annotation.Rotation is null ||
                        annotation.SecondaryPoint is null)
                    {
                        return false;
                    }

                    dbText.Position = ToAcad(annotation.Position.Value);
                    dbText.AlignmentPoint = ToAcad(annotation.SecondaryPoint.Value);
                    dbText.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.MLeader when entity is MLeader leader:
                    return TryRestoreMLeader(leader, annotation, editor);

                case RoofUnsupportedStretchAnnotationKind.BlockReference when entity is BlockReference block:
                    if (annotation.Position is null || annotation.Rotation is null)
                    {
                        return false;
                    }

                    block.Position = ToAcad(annotation.Position.Value);
                    block.Rotation = annotation.Rotation.Value;
                    return true;

                case RoofUnsupportedStretchAnnotationKind.Circle when entity is Circle circle:
                    if (annotation.Position is null || annotation.SecondaryScalar is null)
                    {
                        return false;
                    }

                    circle.Center = ToAcad(annotation.Position.Value);
                    circle.Radius = annotation.SecondaryScalar.Value;
                    return true;

                default:
                    return false;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool TryRestorePolyline(
        Polyline polyline,
        RoofUnsupportedStretchAnnotationSnapshotData annotation)
    {
        if (annotation.PolylineVertices is null ||
            annotation.PolylineBulges is null ||
            annotation.PolylineClosed is null ||
            annotation.ElevationMm is null ||
            annotation.PolylineVertices.Count == 0 ||
            annotation.PolylineVertices.Count != annotation.PolylineBulges.Count)
        {
            return false;
        }

        if (polyline.NumberOfVertices == annotation.PolylineVertices.Count)
        {
            for (var i = 0; i < annotation.PolylineVertices.Count; i++)
            {
                var v = annotation.PolylineVertices[i];
                polyline.SetPointAt(i, new Point2d(v.X, v.Y));
                polyline.SetBulgeAt(i, annotation.PolylineBulges[i]);
            }
        }
        else
        {
            while (polyline.NumberOfVertices > 0)
            {
                polyline.RemoveVertexAt(0);
            }

            for (var i = 0; i < annotation.PolylineVertices.Count; i++)
            {
                var v = annotation.PolylineVertices[i];
                polyline.AddVertexAt(i, new Point2d(v.X, v.Y), annotation.PolylineBulges[i], 0d, 0d);
            }
        }

        polyline.Closed = annotation.PolylineClosed.Value;
        polyline.Elevation = annotation.ElevationMm.Value;
        return true;
    }

    private static bool TryRestoreMLeader(
        MLeader leader,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor)
    {
        if (annotation.SecondaryPoint is null || annotation.TertiaryPoint is null)
        {
            return false;
        }

        if (!TryPrepareLiveMLeaderTopology(
                leader,
                annotation,
                editor,
                out var leaderIndex,
                out var lineIndex))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteFallback(
                editor,
                "member-restore",
                "restore-write-failure",
                handle: annotation.EntityHandle,
                kind: "MLeader",
                detail: "topology-mismatch");
#endif
            return false;
        }

        var attachment = ToAcad(annotation.SecondaryPoint.Value);
        var knee = ToAcad(annotation.TertiaryPoint.Value);
#if DEBUG
        var step = "init";
#endif
        try
        {
            // If native STRETCH inserted bend vertices, rebuild the single leader line
            // in place (same MLeader ObjectId) to the canonical two-point KROVY form.
            if (leader.VerticesCount(lineIndex) != 2)
            {
#if DEBUG
                step = "RebuildLeaderLine";
#endif
                leader.RemoveLeaderLine(lineIndex);
                lineIndex = leader.AddLeaderLine(leaderIndex);
                leader.AddFirstVertex(lineIndex, attachment);
                leader.AddLastVertex(lineIndex, knee);
            }

            var unitX = 0d;
            var unitY = 0d;
            var applyDogleg =
                annotation.MLeaderEnableDogleg == true &&
                annotation.Position is { } doglegVector &&
                annotation.SecondaryScalar is { } doglegLength &&
                TimberNativeMLeaderDoglegInputRules.ShouldCallSetDogleg(
                    doglegLength,
                    doglegVector.X,
                    doglegVector.Y,
                    out unitX,
                    out unitY);

            if (applyDogleg)
            {
#if DEBUG
                step = "DoglegLength";
#endif
                leader.DoglegLength = annotation.SecondaryScalar!.Value;
#if DEBUG
                step = "EnableDogleg";
#endif
                leader.EnableDogleg = true;
#if DEBUG
                step = "SetDogleg";
#endif
                leader.SetDogleg(leaderIndex, new Vector3d(unitX, unitY, 0d));
            }
            else if (annotation.MLeaderEnableDogleg == false)
            {
#if DEBUG
                step = "EnableDogleg-false";
#endif
                leader.EnableDogleg = false;
            }

            if (annotation.QuaternaryPoint is { } landing)
            {
                if (leader.ContentType == ContentType.BlockContent)
                {
#if DEBUG
                    step = "BlockPosition";
#endif
                    leader.BlockPosition = ToAcad(landing);
                }
                else if (leader.ContentType == ContentType.MTextContent)
                {
#if DEBUG
                    step = "TextLocation";
#endif
                    leader.TextLocation = ToAcad(landing);
                }
            }

#if DEBUG
            step = "SetLastVertex";
#endif
            leader.SetLastVertex(lineIndex, knee);
#if DEBUG
            step = "SetFirstVertex";
#endif
            leader.SetFirstVertex(lineIndex, attachment);

            if (applyDogleg)
            {
#if DEBUG
                step = "SetDogleg-reassert";
#endif
                leader.SetDogleg(leaderIndex, new Vector3d(unitX, unitY, 0d));
#if DEBUG
                step = "DoglegLength-reassert";
#endif
                leader.DoglegLength = annotation.SecondaryScalar!.Value;
            }

            if (annotation.Rotation is { } blockRotation &&
                leader.ContentType == ContentType.BlockContent)
            {
#if DEBUG
                step = "BlockRotation";
#endif
                leader.BlockRotation = blockRotation;
            }

            if (annotation.QuaternaryPoint is { } landingFinal &&
                leader.ContentType == ContentType.BlockContent)
            {
#if DEBUG
                step = "BlockPosition-reassert";
#endif
                leader.BlockPosition = ToAcad(landingFinal);
#if DEBUG
                step = "SetLastVertex-reassert";
#endif
                leader.SetLastVertex(lineIndex, knee);
#if DEBUG
                step = "SetFirstVertex-reassert";
#endif
                leader.SetFirstVertex(lineIndex, attachment);
            }

            return true;
        }
#if DEBUG
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderWriteFail(
                editor,
                annotation.EntityHandle,
                step,
                leaderIndex,
                lineIndex,
                ex);
            return false;
        }
#else
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
#endif
    }

    private static bool TryPrepareLiveMLeaderTopology(
        MLeader leader,
        RoofUnsupportedStretchAnnotationSnapshotData annotation,
        Autodesk.AutoCAD.EditorInput.Editor? editor,
        out int leaderIndex,
        out int lineIndex)
    {
        leaderIndex = -1;
        lineIndex = -1;
        var snapshotSummary = FormatMLeaderSnapshotTopology(annotation);
        var liveBefore = FormatMLeaderLiveTopology(leader);

        // Same ObjectId: collapse extras to KROVY's one-leader/one-line form
        // (mirrors AutoCadStandaloneFramedItemOnlyAnnotationService.EnsureSingleLeaderLine).
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length == 0)
            {
#if DEBUG
                RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                    editor,
                    annotation.EntityHandle,
                    snapshotSummary,
                    liveBefore + ";reason=no-leaders");
#endif
                return false;
            }

            leaderIndex = leaderIndexes[0];
            for (var i = 1; i < leaderIndexes.Length; i++)
            {
                leader.RemoveLeader(leaderIndexes[i]);
            }

            var lineIndexes = leader.GetLeaderLineIndexes(leaderIndex).Cast<int>().ToArray();
            if (lineIndexes.Length == 0)
            {
                lineIndex = leader.AddLeaderLine(leaderIndex);
            }
            else
            {
                lineIndex = lineIndexes[0];
                for (var i = 1; i < lineIndexes.Length; i++)
                {
                    leader.RemoveLeaderLine(lineIndexes[i]);
                }
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveBefore + ";reason=normalize-exception");
#endif
            return false;
        }

        var liveAfter = FormatMLeaderLiveTopology(leader);
        var liveContent = MapContentKind(leader.ContentType);
        var leadersAfter = leader.GetLeaderIndexes().Cast<int>().ToArray();
        var linesAfter = leadersAfter.Length == 1
            ? leader.GetLeaderLineIndexes(leadersAfter[0]).Cast<int>().ToArray()
            : Array.Empty<int>();

        if (!RoofUnsupportedStretchRecoveryRules.IsRecoverableMLeaderTopology(
                leadersAfter.Length,
                linesAfter.Length,
                annotation.MLeaderContentKind,
                liveContent))
        {
#if DEBUG
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveAfter + ";reason=incompatible");
#endif
            return false;
        }

        leaderIndex = leadersAfter[0];
        lineIndex = linesAfter[0];
#if DEBUG
        if (RoofUnsupportedStretchRecoveryRules.IsIndexOnlyTopologyDrift(
                annotation.MLeaderLeaderIndex,
                annotation.MLeaderLeaderLineIndex,
                leaderIndex,
                lineIndex) ||
            !string.Equals(liveBefore, liveAfter, StringComparison.Ordinal))
        {
            RoofUnsupportedStretchRecoveryDiag.WriteMLeaderTopology(
                editor,
                annotation.EntityHandle,
                snapshotSummary,
                liveAfter + ";recoverable=1");
        }
#endif
        return true;
    }

    private static string FormatMLeaderSnapshotTopology(
        RoofUnsupportedStretchAnnotationSnapshotData annotation) =>
        $"content={annotation.MLeaderContentKind};" +
        $"leaderIdx={annotation.MLeaderLeaderIndex?.ToString() ?? "-"};" +
        $"lineIdx={annotation.MLeaderLeaderLineIndex?.ToString() ?? "-"};" +
        $"dogleg={(annotation.MLeaderEnableDogleg == true ? 1 : 0)}";

    private static string FormatMLeaderLiveTopology(MLeader leader)
    {
        try
        {
            var leaders = leader.GetLeaderIndexes().Cast<int>().ToArray();
            var leaderText = leaders.Length == 0
                ? "-"
                : string.Join(",", leaders);
            var lineParts = new List<string>();
            foreach (var leaderIdx in leaders)
            {
                var lines = leader.GetLeaderLineIndexes(leaderIdx).Cast<int>().ToArray();
                foreach (var lineIdx in lines)
                {
                    lineParts.Add($"{lineIdx}:v{leader.VerticesCount(lineIdx)}");
                }
            }

            return $"content={MapContentKind(leader.ContentType)};" +
                   $"leaders={leaders.Length}[{leaderText}];" +
                   $"lines={lineParts.Count}[{string.Join(",", lineParts)}];" +
                   $"dogleg={(leader.EnableDogleg ? 1 : 0)};" +
                   $"connect={leader.BlockConnectionType}";
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "unreadable";
        }
    }

    private static RoofUnsupportedStretchMLeaderContentKind MapContentKind(ContentType contentType) =>
        contentType switch
        {
            ContentType.BlockContent => RoofUnsupportedStretchMLeaderContentKind.BlockContent,
            ContentType.MTextContent => RoofUnsupportedStretchMLeaderContentKind.MTextContent,
            ContentType.NoneContent => RoofUnsupportedStretchMLeaderContentKind.NoneContent,
            _ => RoofUnsupportedStretchMLeaderContentKind.Unknown,
        };

    private static void RestorePolylineGeometry(
        Polyline owner,
        RoofUnsupportedStretchSourceSnapshotData snapshot)
    {
        var vertices = snapshot.Vertices;
        if (vertices.Count < 3)
        {
            throw new InvalidOperationException("Recovery snapshot requires at least three vertices.");
        }

        if (owner.NumberOfVertices == vertices.Count)
        {
            for (var i = 0; i < vertices.Count; i++)
            {
                owner.SetPointAt(i, new Point2d(vertices[i].X, vertices[i].Y));
                owner.SetBulgeAt(i, 0d);
            }
        }
        else
        {
            while (owner.NumberOfVertices > 0)
            {
                owner.RemoveVertexAt(0);
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                owner.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0d, 0d, 0d);
            }
        }

        owner.Closed = snapshot.IsClosed;
        owner.Elevation = snapshot.ElevationMm;
        var normal = new Vector3d(snapshot.NormalX, snapshot.NormalY, snapshot.NormalZ);
        if (normal.Length > RoofUnsupportedStretchRecoveryRules.NormalTolerance)
        {
            owner.Normal = normal.GetNormal();
        }
    }

    private static bool TryGetEntityByHandle<T>(
        Database database,
        Transaction transaction,
        string handleText,
        out T? entity)
        where T : Entity =>
        TryGetEntityByHandle(database, transaction, handleText, OpenMode.ForRead, out entity);

    private static bool TryGetEntityByHandle<T>(
        Database database,
        Transaction transaction,
        string handleText,
        OpenMode mode,
        out T? entity,
        bool allowErased = false)
        where T : Entity
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
            if (id.IsNull)
            {
                return false;
            }

            if (id.IsErased && !allowErased)
            {
                return false;
            }

            var resolved = allowErased
                ? AutoCadObjectIdAccess.TryGetObjectAllowErased(
                    transaction,
                    id,
                    mode,
                    out entity,
                    database)
                : AutoCadObjectIdAccess.TryGetObject(
                    transaction,
                    id,
                    mode,
                    out entity,
                    database);
            return resolved && entity is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static bool MatchesAnnotationKind(Entity entity, RoofUnsupportedStretchAnnotationKind kind) =>
        kind switch
        {
            RoofUnsupportedStretchAnnotationKind.Line => entity is Line,
            RoofUnsupportedStretchAnnotationKind.Polyline => entity is Polyline,
            RoofUnsupportedStretchAnnotationKind.MText => entity is MText,
            RoofUnsupportedStretchAnnotationKind.DBText => entity is DBText,
            RoofUnsupportedStretchAnnotationKind.MLeader => entity is MLeader,
            RoofUnsupportedStretchAnnotationKind.BlockReference => entity is BlockReference,
            RoofUnsupportedStretchAnnotationKind.Circle => entity is Circle,
            _ => false,
        };

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

    private static RoofSourceChangeClassification Classify(Polyline polyline)
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
        return geometric with
        {
            Kind = RoofSourceChangeEditStatePolicy.EffectiveKind(
                stored.Data.Kind,
                stored.Data.EditState,
                geometric.Kind),
        };
    }

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);
}
