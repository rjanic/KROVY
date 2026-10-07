using System.Globalization;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Coordinates elevation editing for an Ordinary rafter (AUTO or Independent).
///
/// AUTO member + geometry-affecting elevation change:
///   Shows RoofIndependentOrdinaryDetachWindow (existing KROVY wood style).
///   YES: detaches to Independent, applies elevation, rebuilds Physical3D.
///   NO:  exact rollback, returns false.
///
/// AUTO member + reference/display-mode change only (no geometry change):
///   Persists new DisplayReference without any confirmation.
///   No detach. No Physical3D movement. This is a pure display change.
///
/// Independent member + any elevation change:
///   No confirmation. Applies elevation directly, rebuilds Physical3D,
///   preserves IndependentMemberId.
///
/// Plan2D invariant: the Line's StartPoint and EndPoint are never moved by this service.
/// Plan2D Z is never modified.
///
/// DIAGNOSTICS:
///   ROOF_MEMBER_ELEVATION_STATE — reads current state
///   ROOF_ORDINARY_ELEVATION_LIFECYCLE — full apply lifecycle
/// </summary>
internal static class RoofOrdinaryElevationLifecycleService
{
    // -------------------------------------------------------------------------
    // Read current elevation state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Read the current elevation state for an Ordinary member (AUTO or Independent).
    ///
    /// Priority (never invents synthetic 0/0/0 for a valid existing member):
    ///   1. Persisted StructuralMemberElevationState v1 XRecord
    ///   2. Physical Build State v2 XRecord on the Line → derive
    ///   3. AUTO: live roof provenance capture (TryCaptureOrdinaryBuildState) → derive
    ///   4. Independent: migrate/measure current Physical3D package → derive
    ///   5. Fail loudly (return false) — caller must NOT invent zero defaults
    ///
    /// Opening AK_EDIT for display must not write Elevation XRecord (read-only resolve).
    /// </summary>
    public static bool TryReadCurrentState(
        Line line,
        Transaction transaction,
        out StructuralMemberElevationState? state,
        out string stateSource)
    {
        state = null;
        stateSource = "none";

        var plan = RoofIndependentOrdinaryPhysicalStateService.Axis(line);
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength < 1.0d)
        {
            stateSource = "degenerate_plan";
            TraceFailed(line, null, "AUTO", stateSource, "degenerate_plan");
            return false;
        }

        var generated = RoofGeneratedTimberStore.Read(line).Data;
        var independent = RoofIndependentOrdinaryTimberStore.Read(line);
        var isAuto = generated is { MemberKind: RoofGeneratedTimberKind.Rafter } && independent is null;
        var isIndep = independent is { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine } &&
                      generated is null;
        if (!isAuto && !isIndep)
        {
            stateSource = "not_ordinary";
            TraceFailed(line, null, "unknown", stateSource, "not_ordinary");
            return false;
        }

        var memberKind = isAuto ? "AUTO" : "Independent";
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        if (!metadata.TryRead(line, out var timber) || timber is null || timber.HeightMm <= 0d)
        {
            stateSource = "timber_metadata_unavailable";
            TraceFailed(line, null, memberKind, stateSource, "timber_metadata_unavailable");
            return false;
        }

        // 1. Persisted elevation XRecord (first priority).
        var persisted = StructuralMemberElevationStore.Read(line, transaction);
        if (persisted is not null)
        {
            if (!IsAcceptableResolvedState(persisted, planLength, timber.SlopeDegrees))
            {
                stateSource = "persisted_elevation_v1_implausible";
                TraceFailed(line, persisted, memberKind, stateSource, "implausible_vs_known_pitch");
                return false;
            }
            state = persisted;
            stateSource = "persisted_elevation_v1";
            TraceResolved(line, state, memberKind, stateSource, planLength, timber.HeightMm);
            return true;
        }

        // 2. Physical Build State v2 already on the Line.
        var storedBuild = RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction);
        if (storedBuild is not null)
        {
            if (StructuralMemberElevationRules.TryDeriveFromBuildState(storedBuild, plan, out state) &&
                state is not null &&
                IsAcceptableResolvedState(state, planLength, timber.SlopeDegrees))
            {
                stateSource = "persistent_build_state_v2";
                TraceResolved(line, state, memberKind, stateSource, planLength, timber.HeightMm);
                return true;
            }
            // Fall through to live capture / measure — do not invent zero.
        }

        var database = line.Database;
        var owner = FindOwner(database, transaction, line);

        // 3. AUTO: capture from current roof / Physical3D package.
        if (isAuto && owner is not null && generated is not null)
        {
            var key = RoofGeneratedMemberKey.From(generated);
            if (RoofOrdinaryRafterSolidMaterializationService.TryCaptureOrdinaryBuildState(
                    database, transaction, owner, key, plan, out var captured, historicalIndependent: false) &&
                captured is not null)
            {
                var solid = FindAutoSolid(database, transaction, line, generated);
                if (solid is not null &&
                    RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, captured, out var framed) &&
                    framed is not null)
                    captured = framed;

                if (StructuralMemberElevationRules.TryDeriveFromBuildState(captured, plan, out state) &&
                    state is not null &&
                    IsAcceptableResolvedState(state, planLength, timber.SlopeDegrees))
                {
                    stateSource = "auto_current_physical_package";
                    TraceResolved(line, state, memberKind, stateSource, planLength, timber.HeightMm);
                    return true;
                }
            }

            stateSource = "unresolved";
            TraceFailed(line, state, memberKind, stateSource, "auto_capture_or_derive_failed");
            return false;
        }

        // 4. Independent: migrate / measure current Physical3D package (in-memory only).
        if (isIndep)
        {
            var solid = FindIndependentSolid(database, transaction, independent!);
            if (RoofIndependentOrdinaryPhysicalStateService.TryMigrate(
                    database, transaction, line, solid, owner, timber, out var migrated) &&
                migrated is not null &&
                StructuralMemberElevationRules.TryDeriveFromBuildState(migrated, plan, out state) &&
                state is not null &&
                IsAcceptableResolvedState(state, planLength, timber.SlopeDegrees))
            {
                stateSource = "measured_member_package";
                TraceResolved(line, state, memberKind, stateSource, planLength, timber.HeightMm);
                return true;
            }

            stateSource = "unresolved";
            TraceFailed(line, state, memberKind, stateSource, "independent_measure_or_derive_failed");
            return false;
        }

        stateSource = "unresolved";
        TraceFailed(line, null, memberKind, stateSource, "owner_or_package_unavailable");
        return false;
    }

    private static bool IsAcceptableResolvedState(
        StructuralMemberElevationState state,
        double planLengthMm,
        double knownPitchDegrees) =>
        StructuralMemberElevationRules.IsValid(state) &&
        StructuralMemberElevationRules.IsPlausibleForKnownPitch(state, planLengthMm, knownPitchDegrees);

    // -------------------------------------------------------------------------
    // Apply elevation edit
    // -------------------------------------------------------------------------

    /// <summary>
    /// Apply an elevation edit to an Ordinary member (AUTO or Independent).
    ///
    /// Returns ApplyResult indicating what happened.
    /// The caller must commit the document transaction on success.
    /// On failure, the state is guaranteed to be unchanged (rollback-safe).
    ///
    /// geometryChanged: true if AxisStart or AxisEnd differs from current by > tolerance.
    /// displayOnlyChange: datum/mode change without geometry change.
    /// </summary>
    public static ApplyResult TryApply(
        Document document,
        Line line,
        StructuralMemberElevationState requested,
        StructuralMemberElevationState? current,
        bool geometryChanged,
        SettingsUiPreferences uiPrefs)
    {
        if (!StructuralMemberElevationRules.IsValid(requested))
            return ApplyResult.InvalidInput;

        var isAuto = RoofGeneratedTimberStore.Read(line).Data is
            { MemberKind: RoofGeneratedTimberKind.Rafter };
        var isIndependent = RoofIndependentOrdinaryTimberStore.Read(line) is
            { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine };

        if (!isAuto && !isIndependent)
            return ApplyResult.NotOrdinaryRafter;

        // Display-only change on AUTO: just update the DisplayReference in XRecord.
        // No confirmation, no detach, no Physical3D movement.
        if (isAuto && !geometryChanged)
        {
            using var docLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            line.UpgradeOpen();
            StructuralMemberElevationStore.Write(line, transaction, requested);
            transaction.Commit();
            EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                detachRequired: false, decision: "auto_display_only", result: "pass");
            return ApplyResult.DisplayOnlyApplied;
        }

        // Geometry-affecting edit on AUTO: confirm detach.
        if (isAuto && geometryChanged)
        {
            var confirmed = uiPrefs.Warnings.ConfirmAutomaticMemberDetach == false
                ? true  // auto-accept when configured
                : ShowDetachDialog();
            if (!confirmed)
            {
                EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                    detachRequired: true, decision: "user_cancelled_detach", result: "no");
                return ApplyResult.UserCancelledDetach;
            }

            // Perform detach + elevation apply in one transaction.
            using var docLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            try
            {
                var owner = FindOwner(document.Database, transaction, line);
                if (owner is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: true, decision: "owner_not_found", result: "fail");
                    return ApplyResult.OwnerNotFound;
                }
                var plan = RoofIndependentOrdinaryPhysicalStateService.Axis(line);
                // Capture build state before detach.
                if (!RoofOrdinaryRafterSolidMaterializationService.TryCaptureOrdinaryBuildState(
                        document.Database, transaction, owner,
                        RoofGeneratedMemberKey.From(RoofGeneratedTimberStore.Read(line).Data!),
                        plan, out var capturedState) || capturedState is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: true, decision: "build_state_capture_failed", result: "fail");
                    return ApplyResult.BuildStateUnavailable;
                }
                // Patch build state for new elevation.
                if (!StructuralMemberElevationRules.TryPatchBuildStateForElevation(
                        capturedState, plan, requested, out var patchedState) || patchedState is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: true, decision: "elevation_patch_failed", result: "fail");
                    return ApplyResult.ElevationPatchFailed;
                }
                // Detach: transfers ownership to Independent.
                line.UpgradeOpen();
                if (!RoofIndependentOrdinaryDetachService.TryDetach(
                        document, transaction, owner, line,
                        movePhysicalByPlanDelta: false,
                        planDelta: new Vector3d(0, 0, 0),
                        nativeModifiedIds: Array.Empty<ObjectId>(),
                        acceptedBuildState: patchedState))
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: true, decision: "detach_service_failed", result: "fail");
                    return ApplyResult.DetachFailed;
                }
                // Rebuild Physical3D with new elevation; persist the post-rebuild measured frame.
                if (!RebuildPhysical(document, transaction, line, patchedState, plan, requested,
                        out var acceptedState) ||
                    acceptedState is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: true, decision: "physical_rebuild_failed", result: "fail");
                    return ApplyResult.PhysicalRebuildFailed;
                }
                // Persist elevation XRecord + authoritative build state on the (now Independent) line.
                StructuralMemberElevationStore.Write(line, transaction, requested);
                RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, acceptedState);
                // Same post-accept annotation path as Ordinary GRIP/LENGTHEN/ROTATE Accept.
                SynchronizeAnnotationsAfterAcceptedElevation(document, transaction, line);
                transaction.Commit();
                EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                    detachRequired: true, decision: "detached_and_rebuilt", result: "pass");
                return ApplyResult.AutoDetachedAndApplied;
            }
            catch
            {
                // Transaction rolls back automatically on dispose.
                throw;
            }
        }

        // Independent member: apply directly, no confirmation.
        // Missing Physical Build State v2 → same lazy migration as GRIP/LENGTHEN/MOVE.
        if (isIndependent)
        {
            using var docLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var plan = RoofIndependentOrdinaryPhysicalStateService.Axis(line);
            var buildState = RoofOrdinaryPhysicalBuildStateStore.Read(line, transaction);
            var migrated = false;
            if (buildState is null)
            {
                var metadata = new AutoCadTimberElementMetadataStore(transaction);
                if (!metadata.TryRead(line, out var timber) || timber is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: false, decision: "independent_no_build_state", result: "fail",
                        extra: "buildStateSource=unavailable reason=timber_metadata_unavailable");
                    return ApplyResult.BuildStateUnavailable;
                }

                var identity = RoofIndependentOrdinaryTimberStore.Read(line);
                if (identity is not { EntityRole: RoofIndependentOrdinaryEntityRole.PlanLine })
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: false, decision: "independent_no_build_state", result: "fail",
                        extra: "buildStateSource=unavailable reason=independent_identity_unavailable");
                    return ApplyResult.BuildStateUnavailable;
                }

                var solid = FindIndependentSolid(document.Database, transaction, identity);
                var owner = FindOwner(document.Database, transaction, line);
                if (!RoofIndependentOrdinaryPhysicalStateService.TryMigrate(
                        document.Database, transaction, line, solid, owner, timber,
                        out buildState) ||
                    buildState is null)
                {
                    EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                        detachRequired: false, decision: "independent_no_build_state", result: "fail",
                        extra: "buildStateSource=unavailable reason=independent_migration_failed");
                    return ApplyResult.BuildStateUnavailable;
                }

                migrated = true;
            }

            if (!StructuralMemberElevationRules.TryPatchBuildStateForElevation(
                    buildState, plan, requested, out var patchedState) || patchedState is null)
            {
                EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                    detachRequired: false, decision: "elevation_patch_failed", result: "fail",
                    extra: migrated ? "buildStateSource=migrated_member_package" : null);
                return ApplyResult.ElevationPatchFailed;
            }

            var independentIdBefore = RoofIndependentOrdinaryTimberStore.Read(line)?.IndependentMemberId;
            line.UpgradeOpen();
            if (!RebuildPhysical(document, transaction, line, patchedState, plan, requested,
                    out var acceptedState) ||
                acceptedState is null)
            {
                EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                    detachRequired: false, decision: "physical_rebuild_failed", result: "fail",
                    extra: migrated ? "buildStateSource=migrated_member_package" : null);
                return ApplyResult.PhysicalRebuildFailed;
            }

            // Persist elevation + post-rebuild measured build state (same contract as GRIP Accept).
            StructuralMemberElevationStore.Write(line, transaction, requested);
            RoofIndependentOrdinaryPhysicalStateService.Persist(line, transaction, acceptedState);
            var independentIdAfter = RoofIndependentOrdinaryTimberStore.Read(line)?.IndependentMemberId;
            if (!string.Equals(independentIdBefore, independentIdAfter, StringComparison.Ordinal))
            {
                EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                    detachRequired: false, decision: "independent_id_changed", result: "fail");
                return ApplyResult.PhysicalRebuildFailed;
            }

            // Same post-accept annotation path as Ordinary GRIP/LENGTHEN/ROTATE Accept.
            // Without this, SlopeDegrees metadata updates but the yellow drawing annotation stays stale.
            SynchronizeAnnotationsAfterAcceptedElevation(document, transaction, line);

            transaction.Commit();
            EmitLifecycleDiag(document, line, current, requested, geometryChanged,
                detachRequired: false,
                decision: migrated ? "independent_applied_after_migration" : "independent_applied",
                result: "pass",
                extra: migrated
                    ? "buildStateSource=migrated_member_package migrationPersisted=True sectionFrameCaptured=True annotationsSynced=True"
                    : "buildStateSource=persistent_member_xrecord sectionFrameCaptured=True annotationsSynced=True");
            return ApplyResult.IndependentApplied;
        }

        return ApplyResult.NotOrdinaryRafter;
    }

    // -------------------------------------------------------------------------
    // Physical3D rebuild
    // -------------------------------------------------------------------------

    private static bool RebuildPhysical(
        Document document,
        Transaction transaction,
        Line line,
        RoofOrdinaryPhysicalBuildState patchedState,
        RoofSegment3D plan,
        StructuralMemberElevationState elevation,
        out RoofOrdinaryPhysicalBuildState? acceptedState)
    {
        acceptedState = null;
        // Same Independent builder contract as GRIP/LENGTHEN/ROTATE Accept.
        if (!RoofOrdinaryPhysicalBuildStateRules.TryBuild(
                patchedState, plan, out var physical, out var updated, out var reason,
                useIndependentHorizontalFrame: true) || physical is null || updated is null)
        {
#if DEBUG
            document.Editor.WriteMessage($"\nROOF_ORDINARY_ELEVATION_LIFECYCLE physicalBuildFailed={reason}");
#endif
            return false;
        }

        // Find the existing solid owned by this line.
        var identity = RoofIndependentOrdinaryTimberStore.Read(line);
        if (identity is null) return false;

        ObjectId? existingSolidId = null;
        var database = document.Database;
        var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)transaction.GetObject(
            blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (id.IsErased) continue;
            var entity = transaction.GetObject(id, OpenMode.ForRead) as Solid3d;
            if (entity is null) continue;
            var indep = RoofIndependentOrdinaryTimberStore.Read(entity);
            if (indep?.IndependentMemberId == identity.IndependentMemberId &&
                indep.EntityRole == RoofIndependentOrdinaryEntityRole.PhysicalSolid)
            {
                existingSolidId = id;
                break;
            }
        }

        if (existingSolidId is null)
        {
            // No solid exists yet; this can happen for newly detached members in drawings
            // without Physical3D enabled. Persist the accepted builder state only when
            // Elevation pitch and section-frame pitch agree.
            if (!StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(
                    elevation, plan, updated))
            {
#if DEBUG
                document.Editor.WriteMessage(
                    "\nROOF_ORDINARY_ELEVATION_LIFECYCLE physicalRebuild=fail reason=pitch_mismatch_no_solid" +
                    $" target={StructuralMemberElevationRules.AbsolutePitchDegrees(StructuralMemberElevationRules.DeriveSlopeDegrees(elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, plan.Start.DistanceTo(plan.End))):R}" +
                    $" stored={updated.PitchDegrees:R}" +
                    $" frame={StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(updated.SectionFrame!.LongitudinalAxis):R}");
#endif
                return false;
            }

            var planLenNoSolid = plan.Start.DistanceTo(plan.End);
            var targetPitchNoSolid = StructuralMemberElevationRules.AbsolutePitchDegrees(
                StructuralMemberElevationRules.DeriveSlopeDegrees(
                    elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, planLenNoSolid));
            var metadataNoSolid = new AutoCadTimberElementMetadataStore(transaction);
            if (metadataNoSolid.TryRead(line, out var timberNoSolid) && timberNoSolid is not null)
            {
                var fallReversedNoSolid =
                    StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
                        elevation.AxisStartElevationMm, elevation.AxisEndElevationMm);
                metadataNoSolid.Write(line, timberNoSolid with
                {
                    SlopeDegrees = targetPitchNoSolid,
                    IsSlopeDirectionReversed = fallReversedNoSolid,
                });
            }

            acceptedState = updated;
            return true;
        }

        var solid = (Solid3d)transaction.GetObject(existingSolidId.Value, OpenMode.ForWrite);
        using var rebuilt = RoofOrdinaryRafterSolidMaterializationService.MaterializeOrdinaryMember(physical);
        using var xdata = solid.XData;
        var layerId = solid.LayerId;
        var visible = solid.Visible;
        solid.CopyFrom(rebuilt);
        solid.LayerId = layerId;
        solid.Visible = visible;
        if (xdata is not null) solid.XData = xdata;

        // Authoritative post-rebuild section frame — same contract Detach/GRIP use.
        // Must match the requested Elevation spatial pitch; capturing a stale roof
        // pitch (e.g. 45°) while PitchDegrees says 50° is not a successful Apply.
        if (!RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, updated, out var captured) ||
            captured is null)
        {
#if DEBUG
            document.Editor.WriteMessage(
                "\nROOF_ORDINARY_ELEVATION_LIFECYCLE physicalRebuild=fail reason=section_frame_unresolved");
#endif
            return false;
        }

        var planLen = plan.Start.DistanceTo(plan.End);
        var targetPitch = StructuralMemberElevationRules.AbsolutePitchDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                elevation.AxisStartElevationMm, elevation.AxisEndElevationMm, planLen));
        var measuredPitch = StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
            captured.SectionFrame!.LongitudinalAxis);
        if (!StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(elevation, plan, captured))
        {
#if DEBUG
            document.Editor.WriteMessage(
                "\nROOF_ORDINARY_ELEVATION_LIFECYCLE physicalRebuild=fail reason=pitch_mismatch" +
                $" targetSpatialPitch={targetPitch:R}" +
                $" measuredPhysicalPitch={measuredPitch:R}" +
                $" storedPitch={captured.PitchDegrees:R}" +
                $" storedFramePitch={measuredPitch:R}");
#endif
            return false;
        }

        acceptedState = captured;

        // Update timber slope + fall direction from Elevation (authoritative).
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        if (metadataStore.TryRead(line, out var timber) && timber is not null)
        {
            var fallReversed =
                StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
                    elevation.AxisStartElevationMm, elevation.AxisEndElevationMm);
            metadataStore.Write(line, timber with
            {
                SlopeDegrees = targetPitch,
                IsSlopeDirectionReversed = fallReversed,
            });
        }

#if DEBUG
        document.Editor.WriteMessage(
            $"\nROOF_ORDINARY_ELEVATION_LIFECYCLE physicalRebuild=pass" +
            $" line={line.Handle} solid={solid.Handle}" +
            $" axisStart={elevation.AxisStartElevationMm:R} axisEnd={elevation.AxisEndElevationMm:R}" +
            $" targetSpatialPitch={targetPitch:R} measuredPhysicalPitch={measuredPitch:R}" +
            $" storedPitch={captured.PitchDegrees:R} sectionFrameCaptured=True");
#endif
        return true;
    }

    /// <summary>
    /// Shared Ordinary lifecycle post-accept annotation sync used by GRIP/LENGTHEN/ROTATE Accept.
    /// Elevation Apply must call this after geometry + Elevation + Build State are persisted so the
    /// yellow slope annotation / labels / designation refresh without a second command.
    /// </summary>
    private static void SynchronizeAnnotationsAfterAcceptedElevation(
        Document document,
        Transaction transaction,
        Line line)
    {
        var metadata = new AutoCadTimberElementMetadataStore(transaction);
        if (!metadata.TryRead(line, out var timber) || timber is null)
            throw new InvalidOperationException(
                "Ordinary Elevation annotation sync: timber metadata unavailable.");

        var profile = TimberElementDefaultProfileStore.Load();
        var roundingStep = profile.GetCuttingLengthRoundingStepMm();
        var previousElementId = timber.ElementId;
        var sync = TimberElementItemIdentityService.SynchronizeElementIdsDetailed(
            document.Database, transaction, metadata, new[] { line.ObjectId }, roundingStep);
        if (!sync.DataById.TryGetValue(line.ObjectId, out var data))
            data = timber;

        TimberAnnotationService.EnsureForElement(
            document.Database,
            transaction,
            line,
            data,
            AutoCadAnnotationPresentationBatchContext.Create(document.Database, transaction, profile),
            previousElementId,
            roundingStep);

        if (RoofIndependentOrdinaryTimberStore.Read(line) is { } identity)
        {
            foreach (var id in FindAnnotationsOwnedByLine(document.Database, transaction, line.Handle.ToString()))
            {
                RoofIndependentOrdinaryTimberStore.Write(
                    (Entity)transaction.GetObject(id, OpenMode.ForWrite),
                    transaction,
                    identity with { EntityRole = RoofIndependentOrdinaryEntityRole.Annotation });
            }
        }

#if DEBUG
        document.Editor.WriteMessage(
            $"\nROOF_ORDINARY_ELEVATION_LIFECYCLE annotationsSynced=True line={line.Handle}" +
            $" slopeDegrees={data.SlopeDegrees:R} elementId={data.ElementId}");
#endif
    }

    private static IReadOnlyList<ObjectId> FindAnnotationsOwnedByLine(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        var results = new List<ObjectId>();
        var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)transaction.GetObject(
            blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (id.IsErased) continue;
            var entity = transaction.GetObject(id, OpenMode.ForRead) as Entity;
            if (entity is null) continue;
            if (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var source) &&
                string.Equals(source, sourceHandle, StringComparison.Ordinal))
                results.Add(id);
        }
        return results;
    }

    // -------------------------------------------------------------------------
    // Dialog
    // -------------------------------------------------------------------------

    private static bool ShowDetachDialog()
    {
        var preferences = SettingsUiPreferencesStore.Load();
        if (!preferences.Warnings.ConfirmAutomaticMemberDetach) return true;

        var window = new RoofIndependentOrdinaryDetachWindow();
        var owner = AcApp.MainWindow.Handle;
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        helper.Owner = owner;
        var result = window.ShowDialog() == true;
        return result;
    }

    // -------------------------------------------------------------------------
    // Owner / physical package resolution
    // -------------------------------------------------------------------------

    private static Polyline? FindOwner(Database database, Transaction transaction, Line line)
    {
        var generated = RoofGeneratedTimberStore.Read(line).Data;
        var independent = RoofIndependentOrdinaryTimberStore.Read(line);
        var ownerRef = generated?.RoofOwnerReference ?? independent?.SourceRoofReference;
        if (ownerRef is null) return null;
        if (!RoofGeneratedTimberStore.TryResolveId(database, ownerRef, out var ownerId))
            return null;
        return transaction.GetObject(ownerId, OpenMode.ForRead) as Polyline;
    }

    private static Solid3d? FindAutoSolid(
        Database database,
        Transaction transaction,
        Line line,
        RoofGeneratedTimberData generated)
    {
        if (!RoofOrdinaryRafterSolidMaterializationService.TryGetPlanPhysicalIdentity(
                line, generated.RoofOwnerReference, out var physicalKey))
            return null;
        Solid3d? match = null;
        foreach (var id in RoofPhysical3DGeneratedStore.FindByOwner(
                     database, transaction, generated.RoofOwnerReference))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Solid3d solid || solid.IsErased)
                continue;
            if (RoofPhysical3DGeneratedStore.Read(solid).Data is not
                { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } data)
                continue;
            if (!string.Equals(data.StructuralId, physicalKey, StringComparison.Ordinal))
                continue;
            if (match is not null) return null; // ambiguous
            match = solid;
        }
        return match;
    }

    private static Solid3d? FindIndependentSolid(
        Database database,
        Transaction transaction,
        RoofIndependentOrdinaryTimberData identity)
    {
        Solid3d? match = null;
        var model = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForRead);
        foreach (ObjectId id in model)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Solid3d solid ||
                solid.IsErased)
                continue;
            if (RoofIndependentOrdinaryTimberStore.Read(solid) is not
                { EntityRole: RoofIndependentOrdinaryEntityRole.PhysicalSolid } paired)
                continue;
            if (paired.IndependentMemberId != identity.IndependentMemberId)
                continue;
            if (match is not null) return null;
            match = solid;
        }
        return match;
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    private static void TraceResolved(
        Line line,
        StructuralMemberElevationState state,
        string memberKind,
        string source,
        double planLengthMm,
        double heightMm)
    {
#if DEBUG
        var slope = StructuralMemberElevationRules.DeriveSlopeDegrees(
            state.AxisStartElevationMm, state.AxisEndElevationMm, planLengthMm);
        var haz = StructuralMemberElevationRules.HeightAxisZ(
            StructuralMemberElevationRules.AbsolutePitchDegrees(slope));
        var lowerAxis = Math.Min(state.AxisStartElevationMm, state.AxisEndElevationMm);
        var upperAxis = Math.Max(state.AxisStartElevationMm, state.AxisEndElevationMm);
        var displayLower = StructuralMemberElevationRules.ToDisplayElevationMm(
            lowerAxis, state.DisplayReference, heightMm, haz);
        var displayUpper = StructuralMemberElevationRules.ToDisplayElevationMm(
            upperAxis, state.DisplayReference, heightMm, haz);
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            $"\nROOF_MEMBER_ELEVATION_STATE line={line.Handle} state={memberKind}" +
            $" reference={state.DisplayReference}" +
            $" axisStartZMm={state.AxisStartElevationMm:R} axisEndZMm={state.AxisEndElevationMm:R}" +
            $" displayLowerZMm={displayLower:R} displayUpperZMm={displayUpper:R}" +
            $" slopeDegrees={slope:R} stateSource={source}" +
            $" schemaVersion={state.SchemaVersion} result=ok");
#endif
    }

    private static void TraceFailed(
        Line line,
        StructuralMemberElevationState? state,
        string memberKind,
        string source,
        string reason)
    {
#if DEBUG
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            $"\nROOF_MEMBER_ELEVATION_STATE line={line.Handle} state={memberKind}" +
            $" reference={(state?.DisplayReference.ToString() ?? "-")}" +
            $" axisStartZMm={state?.AxisStartElevationMm:R} axisEndZMm={state?.AxisEndElevationMm:R}" +
            $" stateSource={source} schemaVersion={state?.SchemaVersion ?? 0}" +
            $" result=failed reason={reason}");
#endif
    }

    private static void EmitLifecycleDiag(
        Document document,
        Line line,
        StructuralMemberElevationState? before,
        StructuralMemberElevationState? after,
        bool geometryChanged,
        bool detachRequired,
        string decision,
        string result,
        string? extra = null)
    {
#if DEBUG
        var dx = 0d;
        var dy = 0d;
        var plan = RoofIndependentOrdinaryPhysicalStateService.Axis(line);
        dx = plan.End.X - plan.Start.X; dy = plan.End.Y - plan.Start.Y;
        var planLen = Math.Sqrt(dx * dx + dy * dy);
        var slopeBefore = before is not null && planLen > 1e-7
            ? StructuralMemberElevationRules.DeriveSlopeDegrees(
                before.AxisStartElevationMm, before.AxisEndElevationMm, planLen) : 0d;
        var slopeAfter = after is not null && planLen > 1e-7
            ? StructuralMemberElevationRules.DeriveSlopeDegrees(
                after.AxisStartElevationMm, after.AxisEndElevationMm, planLen) : 0d;
        document.Editor.WriteMessage(
            $"\nROOF_ORDINARY_ELEVATION_LIFECYCLE line={line.Handle}" +
            $" stateBefore={(before is null ? "none" : before.DisplayReference.ToString())}" +
            $" referenceAfter={(after is null ? "none" : after.DisplayReference.ToString())}" +
            $" geometryChanged={geometryChanged} detachRequired={detachRequired}" +
            $" decision={decision}" +
            $" axisStartZBefore={before?.AxisStartElevationMm:R} axisEndZBefore={before?.AxisEndElevationMm:R}" +
            $" axisStartZAfter={after?.AxisStartElevationMm:R} axisEndZAfter={after?.AxisEndElevationMm:R}" +
            $" slopeBefore={slopeBefore:R} slopeAfter={slopeAfter:R}" +
            $" result={result}" +
            (string.IsNullOrWhiteSpace(extra) ? string.Empty : " " + extra));
#endif
    }

    // -------------------------------------------------------------------------
    // Result type
    // -------------------------------------------------------------------------

    internal enum ApplyResult
    {
        /// <summary>AUTO display/reference change applied; no detach.</summary>
        DisplayOnlyApplied,
        /// <summary>AUTO member detached and elevation applied with Physical3D rebuild.</summary>
        AutoDetachedAndApplied,
        /// <summary>Independent elevation applied with Physical3D rebuild.</summary>
        IndependentApplied,
        /// <summary>User cancelled the detach confirmation dialog.</summary>
        UserCancelledDetach,
        /// <summary>Inputs are invalid.</summary>
        InvalidInput,
        /// <summary>The line is not an Ordinary rafter.</summary>
        NotOrdinaryRafter,
        /// <summary>AUTO roof owner not found.</summary>
        OwnerNotFound,
        /// <summary>Physical Build State could not be captured or read.</summary>
        BuildStateUnavailable,
        /// <summary>Elevation patch of Build State failed.</summary>
        ElevationPatchFailed,
        /// <summary>Detach service returned false.</summary>
        DetachFailed,
        /// <summary>Physical3D rebuild failed.</summary>
        PhysicalRebuildFailed,
    }
}
