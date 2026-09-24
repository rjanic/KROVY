using AcKrovy.AutoCAD.Settings;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Headless automatic-purlin regeneration for supported roof lifecycle changes.
/// Regenerates only when a persisted Purlin layout exists; never invents defaults
/// and never opens AK_ROOF_PURLINS. Callers own the ambient transaction.
/// </summary>
internal static class RoofAutomaticPurlinLiveRegenerationService
{
    internal const string DiagnosticToken = "ROOF_PURLIN_LIVE_REGEN";
    internal const string PreflightDiagnosticToken = "ROOF_PURLIN_PREFLIGHT";
    internal const string LocalizationKeyElevationOutside =
        "Command_RoofEdit_PurlinElevationOutsideRoof";
    internal const string LocalizationKeyLayoutIncompatible =
        "Command_RoofEdit_PurlinLayoutIncompatible";

    public enum Outcome
    {
        /// <summary>No persisted layout — leave the drawing unchanged.</summary>
        NotConfigured = 0,
        Regenerated = 1,
        Failed = 2,
    }

    public enum PreflightOutcome
    {
        NotConfigured = 0,
        Valid = 1,
        Invalid = 2,
    }

    public readonly record struct Result(
        Outcome Outcome,
        string OwnerReference,
        string Trigger,
        RoofAutomaticPurlinMaterializationResult? Materialization,
        string FailureReason)
    {
        public bool IsSuccess =>
            Outcome is Outcome.NotConfigured or Outcome.Regenerated;
    }

    public readonly record struct PreflightResult(
        PreflightOutcome Outcome,
        string OwnerReference,
        RoofAutomaticPurlinPlanError PlanError,
        string? FailedLayoutItemId,
        string LocalizationKey,
        string FailureReason)
    {
        public bool IsSuccess =>
            Outcome is PreflightOutcome.NotConfigured or PreflightOutcome.Valid;
    }

    /// <summary>
    /// Dry-runs the Core purlin planner against the proposed roof geometry using
    /// the persisted layout/datum (BottomEdge values adapted to preserve plan
    /// stations when <paramref name="previousGeometry"/> pitch differs) and the
    /// current authoritative rafter recipe. Performs no DB writes.
    /// </summary>
    public static PreflightResult TryValidatePersistedLayoutForProposedGeometry(
        Database database,
        Transaction transaction,
        Polyline owner,
        HipRoofGeometry proposedGeometry,
        TimberElementDefaultProfile defaultProfile,
        Editor? editor = null,
        HipRoofGeometry? previousGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(proposedGeometry);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var ownerReference = owner.Handle.ToString();
        var storedLayout = RoofPurlinLayoutStore.Read(owner);
        if (!storedLayout.Exists)
        {
            var skipped = new PreflightResult(
                PreflightOutcome.NotConfigured,
                ownerReference,
                RoofAutomaticPurlinPlanError.None,
                null,
                string.Empty,
                string.Empty);
            WritePreflightDiagnostic(editor, skipped);
            return skipped;
        }

        if (storedLayout.Data is null)
        {
            var malformed = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                RoofAutomaticPurlinPlanError.InvalidLayout,
                null,
                LocalizationKeyLayoutIncompatible,
                "layout-" + storedLayout.Error);
            WritePreflightDiagnostic(editor, malformed);
            return malformed;
        }

        var storedDatum = RoofRelativeElevationDatumStore.Read(owner);
        if (!storedDatum.Exists || storedDatum.Data is null)
        {
            var datumFailure = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum,
                null,
                LocalizationKeyLayoutIncompatible,
                storedDatum.Exists
                    ? "relative-elevation-datum-" + storedDatum.Error
                    : "NoRelativeElevationDatum");
            WritePreflightDiagnostic(editor, datumFailure);
            return datumFailure;
        }

        var boundaryRead = RoofBoundaryIdentityStore.Read(owner);
        if (boundaryRead.Data is null)
        {
            var boundaryFailure = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance,
                null,
                LocalizationKeyLayoutIncompatible,
                "boundary-identity-" + (boundaryRead.Exists ? boundaryRead.Error : "missing"));
            WritePreflightDiagnostic(editor, boundaryFailure);
            return boundaryFailure;
        }

        var sourceInput = RoofPolylineExtractor.Extract(owner);
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
            sourceInput,
            boundaryRead.Data);
        if (!provenance.IsValid)
        {
            var provenanceFailure = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance,
                null,
                LocalizationKeyLayoutIncompatible,
                "boundary-provenance-invalid");
            WritePreflightDiagnostic(editor, provenanceFailure);
            return provenanceFailure;
        }

        if (!RoofAutomaticPurlinPlanningRafterResolver.TryResolve(
                database,
                transaction,
                ownerReference,
                storedLayout.Data,
                defaultProfile,
                out var planningRafter,
                out var rafterFailure,
                editor))
        {
            var rafterFail = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                RoofAutomaticPurlinPlanError.InvalidPhysicalSection,
                null,
                LocalizationKeyLayoutIncompatible,
                rafterFailure);
            WritePreflightDiagnostic(editor, rafterFail);
            return rafterFail;
        }

        var purlinDefaults = TimberElementDefaults.For(
            TimberElementType.Purlin,
            defaultProfile);
        var wallPlateDefaults = TimberElementDefaults.For(
            TimberElementType.WallPlate,
            defaultProfile);
        var planningInput = new RoofAutomaticPurlinPlanningInput(
            storedDatum.Data,
            purlinDefaults.HeightMm,
            planningRafter.HeightMm)
        {
            PurlinWidthMm = purlinDefaults.WidthMm,
            WallPlatesEnabled = storedLayout.Data.WallPlateEnabled,
            WallPlateWidthMm = wallPlateDefaults.WidthMm,
            WallPlateHeightMm = wallPlateDefaults.HeightMm,
        };

        var layoutForPlan = storedLayout.Data;
        if (previousGeometry is not null)
        {
            var adapted = RoofAutomaticPurlinPitchAdaptationRules
                .TryAdaptLayoutPreservingPlanStations(
                    previousGeometry,
                    proposedGeometry,
                    provenance,
                    storedLayout.Data,
                    planningInput);
            if (!adapted.IsValid || adapted.AdaptedLayout is null)
            {
                var adaptFail = new PreflightResult(
                    PreflightOutcome.Invalid,
                    ownerReference,
                    adapted.Error,
                    adapted.FailedLayoutItemId,
                    ResolveLocalizationKey(adapted.Error),
                    "automatic-purlin-pitch-adapt-" + adapted.Error);
                WritePreflightDiagnostic(editor, adaptFail);
                return adaptFail;
            }

            layoutForPlan = adapted.AdaptedLayout;
        }

        var planned = RoofAutomaticPurlinPlanner.Create(
            proposedGeometry,
            provenance,
            layoutForPlan,
            planningInput);
        if (!planned.IsValid || planned.Plan is null)
        {
            var invalid = new PreflightResult(
                PreflightOutcome.Invalid,
                ownerReference,
                planned.Error,
                planned.FailedLayoutItemId,
                ResolveLocalizationKey(planned.Error),
                "automatic-purlin-plan-" + planned.Error);
            WritePreflightDiagnostic(editor, invalid);
            return invalid;
        }

        var valid = new PreflightResult(
            PreflightOutcome.Valid,
            ownerReference,
            RoofAutomaticPurlinPlanError.None,
            null,
            string.Empty,
            string.Empty);
        WritePreflightDiagnostic(editor, valid);
        return valid;
    }

    public static string ResolveLocalizationKey(RoofAutomaticPurlinPlanError error) =>
        error switch
        {
            RoofAutomaticPurlinPlanError.ElevationOutsideRoof or
            RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement or
            RoofAutomaticPurlinPlanError.CriticalEventElevation =>
                LocalizationKeyElevationOutside,
            _ => LocalizationKeyLayoutIncompatible,
        };

    /// <summary>
    /// Regenerates automatic purlins from the owner's persisted layout + datum
    /// using the current roof geometry and recovered rafter recipe height.
    /// When <paramref name="previousGeometry"/> is provided and pitch differs,
    /// BottomEdge placement values are adapted to preserve plan stations and the
    /// adapted layout is persisted before materialization.
    /// </summary>
    public static Result TryRegenerateInTransaction(
        Document document,
        Transaction transaction,
        Polyline owner,
        string trigger,
        TimberElementDefaultProfile? defaultProfile = null,
        ElementLayerProfile? layerProfile = null,
        Editor? editor = null,
        bool syncAssemblyGroup = true,
        HipRoofGeometry? previousGeometry = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(trigger))
        {
            trigger = "unknown";
        }

        var ownerReference = owner.Handle.ToString();
        var storedLayout = RoofPurlinLayoutStore.Read(owner);
        if (!storedLayout.Exists)
        {
            var skipped = new Result(
                Outcome.NotConfigured,
                ownerReference,
                trigger,
                null,
                string.Empty);
            WriteDiagnostic(editor ?? document.Editor, skipped);
            return skipped;
        }

        if (storedLayout.Data is null)
        {
            var malformedLayout = new Result(
                Outcome.Failed,
                ownerReference,
                trigger,
                null,
                "layout-" + storedLayout.Error);
            WriteDiagnostic(editor ?? document.Editor, malformedLayout);
            return malformedLayout;
        }

        var storedDatum = RoofRelativeElevationDatumStore.Read(owner);
        if (!storedDatum.Exists)
        {
            var missingDatum = new Result(
                Outcome.Failed,
                ownerReference,
                trigger,
                null,
                "NoRelativeElevationDatum");
            WriteDiagnostic(editor ?? document.Editor, missingDatum);
            return missingDatum;
        }

        if (storedDatum.Data is null)
        {
            var malformedDatum = new Result(
                Outcome.Failed,
                ownerReference,
                trigger,
                null,
                "relative-elevation-datum-" + storedDatum.Error);
            WriteDiagnostic(editor ?? document.Editor, malformedDatum);
            return malformedDatum;
        }

        defaultProfile ??= TimberElementDefaultProfileStore.Load();
        layerProfile ??= ElementLayerProfileStore.Load();

        var layoutForMaterialize = storedLayout.Data;
        if (previousGeometry is not null)
        {
            var preparation =
                RoofAutomaticPurlinMaterializationService.PrepareInTransaction(
                    document,
                    transaction,
                    owner,
                    out var preparationFailure);
            if (preparation is null)
            {
                var prepFail = new Result(
                    Outcome.Failed,
                    ownerReference,
                    trigger,
                    null,
                    preparationFailure);
                WriteDiagnostic(editor ?? document.Editor, prepFail);
                return prepFail;
            }

            if (!RoofAutomaticPurlinPlanningRafterResolver.TryResolve(
                    document.Database,
                    transaction,
                    ownerReference,
                    storedLayout.Data,
                    defaultProfile,
                    out var planningRafter,
                    out var rafterFailure,
                    editor ?? document.Editor))
            {
                var rafterFail = new Result(
                    Outcome.Failed,
                    ownerReference,
                    trigger,
                    null,
                    rafterFailure);
                WriteDiagnostic(editor ?? document.Editor, rafterFail);
                return rafterFail;
            }

            var boundaryRead = RoofBoundaryIdentityStore.Read(owner);
            if (boundaryRead.Data is null)
            {
                var boundaryFail = new Result(
                    Outcome.Failed,
                    ownerReference,
                    trigger,
                    null,
                    "boundary-identity-missing");
                WriteDiagnostic(editor ?? document.Editor, boundaryFail);
                return boundaryFail;
            }

            var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
                preparation.SourceInput,
                boundaryRead.Data);
            if (!provenance.IsValid)
            {
                var provenanceFail = new Result(
                    Outcome.Failed,
                    ownerReference,
                    trigger,
                    null,
                    "boundary-provenance-invalid");
                WriteDiagnostic(editor ?? document.Editor, provenanceFail);
                return provenanceFail;
            }

            var purlinDefaults = TimberElementDefaults.For(
                TimberElementType.Purlin,
                defaultProfile);
            var wallPlateDefaults = TimberElementDefaults.For(
                TimberElementType.WallPlate,
                defaultProfile);
            var planningInput = new RoofAutomaticPurlinPlanningInput(
                storedDatum.Data,
                purlinDefaults.HeightMm,
                planningRafter.HeightMm)
            {
                PurlinWidthMm = purlinDefaults.WidthMm,
                WallPlatesEnabled = storedLayout.Data.WallPlateEnabled,
                WallPlateWidthMm = wallPlateDefaults.WidthMm,
                WallPlateHeightMm = wallPlateDefaults.HeightMm,
            };

            var adapted = RoofAutomaticPurlinPitchAdaptationRules
                .TryAdaptLayoutPreservingPlanStations(
                    previousGeometry,
                    preparation.HipGeometry,
                    provenance,
                    storedLayout.Data,
                    planningInput);
            if (!adapted.IsValid || adapted.AdaptedLayout is null)
            {
                var adaptFail = new Result(
                    Outcome.Failed,
                    ownerReference,
                    trigger,
                    null,
                    "automatic-purlin-pitch-adapt-" + adapted.Error);
                WriteDiagnostic(editor ?? document.Editor, adaptFail);
                return adaptFail;
            }

            layoutForMaterialize = adapted.AdaptedLayout;
            if (adapted.LayoutChanged)
            {
                RoofPurlinLayoutStore.Write(owner, transaction, layoutForMaterialize);
            }
        }

        var materialization =
            RoofAutomaticPurlinMaterializationService.MaterializeInTransaction(
                document,
                transaction,
                owner,
                layoutForMaterialize,
                storedDatum.Data,
                defaultProfile,
                layerProfile,
                includeWallPlates: layoutForMaterialize.WallPlateEnabled,
                syncAssemblyGroup: syncAssemblyGroup);

        var result = materialization.IsSuccess
            ? new Result(
                Outcome.Regenerated,
                ownerReference,
                trigger,
                materialization,
                string.Empty)
            : new Result(
                Outcome.Failed,
                ownerReference,
                trigger,
                materialization,
                materialization.Result);
        WriteDiagnostic(editor ?? document.Editor, result);
        return result;
    }

#if DEBUG
    private static void WritePreflightDiagnostic(Editor? editor, PreflightResult result)
    {
        editor?.WriteMessage(
            "\n" + PreflightDiagnosticToken +
            $" owner={result.OwnerReference}" +
            $" layout={(result.Outcome == PreflightOutcome.NotConfigured ? "none" : "persisted")}" +
            $" planError={(result.PlanError == RoofAutomaticPurlinPlanError.None ? "-" : result.PlanError.ToString())}" +
            $" failedItem={result.FailedLayoutItemId ?? "-"}" +
            $" result={(result.IsSuccess ? (result.Outcome == PreflightOutcome.NotConfigured ? "skipped-not-configured" : "ok") : result.FailureReason)}");
    }

    private static void WriteDiagnostic(Editor? editor, Result result)
    {
        var materialization = result.Materialization;
        editor?.WriteMessage(
            "\n" + DiagnosticToken +
            $" owner={result.OwnerReference}" +
            $" trigger={result.Trigger}" +
            $" layout={(result.Outcome == Outcome.NotConfigured ? "none" : "persisted")}" +
            $" desired={(materialization?.Desired.ToString() ?? "-")}" +
            $" actual={(materialization?.Actual.ToString() ?? "-")}" +
            $" created={(materialization?.Created.ToString() ?? "-")}" +
            $" updated={(materialization?.Updated.ToString() ?? "-")}" +
            $" staleRemoved={(materialization?.StaleRemoved.ToString() ?? "-")}" +
            $" result={(result.IsSuccess ? (result.Outcome == Outcome.NotConfigured ? "skipped-not-configured" : "ok") : result.FailureReason)}");
    }
#else
    private static void WritePreflightDiagnostic(Editor? editor, PreflightResult result)
    {
        _ = editor;
        _ = result;
    }

    private static void WriteDiagnostic(Editor? editor, Result result)
    {
        _ = editor;
        _ = result;
    }
#endif
}
