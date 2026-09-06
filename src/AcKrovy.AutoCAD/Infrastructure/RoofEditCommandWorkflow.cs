using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// AK_ROOF_EDIT: edits an already-created gable or Monopitch roof through
/// the shared GableRoofGeometryWindow. Persisted Hip roofs use their focused
/// uniform-slope edit dialog. The Gable dialog is seeded from the persisted
/// definition (kind, slopes, ΔH and the
/// persisted semantic direction — never the footprint fallback). Everything
/// before Apply is read-only: transient preview only, no definition write, no
/// display/rafter/annotation/group mutation.
/// Apply rebases the existing definition to the edited physical geometry and
/// replays the canonical rebuild pipeline (display rebuild, generated-set
/// replacement, anchored AttachedManual replay, group/indicator/selectability
/// sync). The create-only persist-conflict path is never used.
/// </summary>
internal static class RoofEditCommandWorkflow
{
    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;

        while (true)
        {
            var prompt = new PromptEntityOptions(
                UiStrings.GetString("Command_RoofEdit_SelectPrompt"));
            var selected = editor.GetEntity(prompt);
            if (selected.Status != PromptStatus.OK)
            {
                return;
            }

            RoofValidationResult validation;
            RoofFootprintInput sourceInput;
            double sourceElevation;
            string ownerReference;
            ObjectId ownerId;
            RoofDefinitionData storedDefinition;
            IRoofGeometry restoredGeometry;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var resolution = RoofOwnerSelectionResolver.Resolve(
                    document.Database,
                    transaction,
                    selected.ObjectId);
                if (!resolution.IsResolved)
                {
                    if (resolution.Error == RoofOwnerSelectionError.UnrelatedObject)
                    {
                        TransientNotificationService.Show(
                            "Command_Roof_InvalidObjectNotificationTitle",
                            "Command_Roof_InvalidObjectNotificationBody");
                    }
                    else
                    {
                        editor.WriteMessage(UiStrings.GetString(
                            "Command_RoofRafters_InvalidRoof"));
                    }
                    continue;
                }

                ownerId = resolution.OwnerId;
                if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline polyline)
                {
                    editor.WriteMessage(UiStrings.GetString("Command_Roof_SelectionOrphan"));
                    continue;
                }

                sourceInput = RoofPolylineExtractor.Extract(polyline);
                validation = RoofFootprintValidator.Validate(sourceInput);
                sourceElevation = RoofPolylineExtractor.GetSourceElevation(polyline);
                ownerReference = polyline.Handle.ToString();
                if (!validation.IsValid || validation.Footprint is null)
                {
                    editor.WriteMessage(UiStrings.GetString(
                        validation.Error == RoofValidationError.OpenLoop
                            ? "Command_Roof_ErrorOpen"
                            : "Command_Roof_ErrorUnsupported"));
                    continue;
                }

                var stored = RoofDefinitionStore.Read(polyline);
                if (stored.Data is null)
                {
                    editor.WriteMessage(UiStrings.GetString(
                        stored.Exists
                            ? "Command_Roof_PersistedInvalid"
                            : "Command_RoofRafters_InvalidRoof"));
                    continue;
                }

                storedDefinition = stored.Data;
                var restored = RoofDefinitionPersistence.Restore(
                    sourceInput,
                    validation.Footprint,
                    stored.Data);
                if (!restored.IsValid || restored.Geometry is null)
                {
                    editor.WriteMessage(UiStrings.GetString(
                        restored.Error == RoofDefinitionRestoreError.StaleFootprint
                            ? "Command_Roof_PersistedStale"
                            : "Command_Roof_PersistedInvalid"));
                    continue;
                }

                restoredGeometry = restored.Geometry;
            }

            RunEditDialog(
                document,
                ownerId,
                ownerReference,
                validation,
                sourceElevation,
                storedDefinition,
                restoredGeometry);
            return;
        }
    }

    private static void RunEditDialog(
        Document document,
        ObjectId ownerId,
        string ownerReference,
        RoofValidationResult validation,
        double sourceElevation,
        RoofDefinitionData storedDefinition,
        IRoofGeometry restoredGeometry)
    {
        var footprint = validation.Footprint!;
        if (storedDefinition.Kind == RoofKind.Hip)
        {
            if (restoredGeometry is HipRoofGeometry hipGeometry)
            {
                RunHipEditDialog(
                    document,
                    ownerId,
                    ownerReference,
                    footprint,
                    sourceElevation,
                    hipGeometry);
            }
            else
            {
                document.Editor.WriteMessage(UiStrings.GetString(
                    "Command_Roof_PersistedInvalid"));
            }
            return;
        }

        var viewModel = new GableRoofGeometryViewModel(
            footprint,
            storedDefinition.Kind,
            isEditMode: true);
        viewModel.SeedFromExistingGeometry(restoredGeometry);
        var dialog = new GableRoofGeometryWindow(
            viewModel,
            SettingsUiPreferencesStore.Load().Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        try
        {
            while (!dialog.IsClosed)
            {
                dialog.PrepareForInteraction();
                _ = AcApp.ShowModalWindow(dialog);
                switch (dialog.RequestedAction)
                {
                    case GableRoofGeometryDialogAction.PickRidgeDirection:
                        if (TryPromptOrientationDirection(
                                document.Editor,
                                viewModel.SelectedKind,
                                out var direction))
                        {
                            viewModel.SetRidgeDirection(direction);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Preview:
                        if (viewModel.TryGetRoofGeometry(out var previewGeometry) &&
                            previewGeometry is not null)
                        {
                            document.Editor.SetImpliedSelection([ownerId]);
                            document.Editor.UpdateScreen();
                            ShowPreview(document, previewGeometry, sourceElevation);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Apply:
                        if (!viewModel.TryGetRoofGeometry(out var geometry) || geometry is null)
                        {
                            continue;
                        }

                        var outcome = TryApply(
                            document,
                            ownerId,
                            ownerReference,
                            restoredGeometry,
                            geometry,
                            out var failureMessageKey);
                        if (outcome is not null)
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(
                                outcome.Value == RoofGeneratedRafterSetService.ReplacementOutcome.Replaced
                                    ? "Command_RoofEdit_UpdatedFormat"
                                    : GetSoftReplacementMessage(outcome.Value)));
                        }
                        else
                        {
                            document.Editor.WriteMessage(UiStrings.GetString(failureMessageKey));
                        }

                        document.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
                        return;

                    default:
                        return;
                }
            }
        }
        finally
        {
            if (!dialog.IsClosed)
            {
                dialog.Close();
            }
        }
    }

    /// <summary>
    /// Loads a persisted Hip definition into its focused edit UI. Preview and Cancel
    /// stay read-only; Apply enters the shared atomic definition/display pipeline.
    /// </summary>
    private static void RunHipEditDialog(
        Document document,
        ObjectId ownerId,
        string ownerReference,
        RoofFootprint footprint,
        double sourceElevation,
        HipRoofGeometry restoredGeometry)
    {
        var viewModel = new HipRoofPreviewViewModel(
            footprint,
            restoredGeometry.PrimarySlopeDegrees,
            HipRoofDialogMode.Edit);
        var dialog = new HipRoofPreviewWindow(
            viewModel,
            SettingsUiPreferencesStore.Load().Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[AK_ROOF_HIP] edit loaded token=Hip " +
            $"slope={restoredGeometry.PrimarySlopeDegrees:R} owner={ownerReference}");
#endif
        try
        {
            while (!dialog.IsClosed)
            {
                dialog.PrepareForInteraction();
                _ = AcApp.ShowModalWindow(dialog);
                switch (dialog.RequestedAction)
                {
                    case HipRoofPreviewDialogAction.Preview:
                        if (!viewModel.TryGetRoofGeometry(out var previewGeometry) ||
                            previewGeometry is null)
                        {
                            continue;
                        }

                        document.Editor.SetImpliedSelection([ownerId]);
                        document.Editor.UpdateScreen();
                        try
                        {
                            ShowPreview(document, previewGeometry, sourceElevation);
                        }
                        finally
                        {
                            document.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
                        }
                        continue;

                    case HipRoofPreviewDialogAction.Apply:
                        if (!viewModel.TryGetRoofGeometry(out var geometry) || geometry is null)
                        {
                            continue;
                        }

                        var outcome = TryApply(
                            document,
                            ownerId,
                            ownerReference,
                            restoredGeometry,
                            geometry,
                            out var failureMessageKey);
                        document.Editor.WriteMessage(UiStrings.GetString(
                            outcome is not null
                                ? GetSoftReplacementMessage(outcome.Value)
                                : failureMessageKey));
                        document.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
                        return;

                    default:
                        return;
                }
            }
        }
        finally
        {
            if (!dialog.IsClosed)
            {
                dialog.Close();
            }
        }
    }

    /// <summary>
    /// Applies the edited geometry to the EXISTING roof. Re-validates the source,
    /// rebases the persisted definition (schema 5) and rebuilds the permanent display.
    /// Supported Gable and Monopitch roofs also regenerate the generated rafter set
    /// through the proven replacement path and replay anchored AttachedManual children
    /// against their rebuilt anchors. Hip roofs finish with canonical group sync.
    /// A single write transaction; no lock/transaction is held while the dialog is
    /// open or while the transient preview is active.
    /// </summary>
    private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(
        Document document,
        ObjectId ownerId,
        string ownerReference,
        IRoofGeometry selectionGeometry,
        IRoofGeometry newGeometry,
        out string failureMessageKey)
    {
        failureMessageKey = "Command_Roof_PersistFailed";
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            if (transaction.GetObject(ownerId, OpenMode.ForWrite) is not Polyline owner ||
                !string.Equals(
                    owner.Handle.ToString(),
                    ownerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var currentInput = RoofPolylineExtractor.Extract(owner);
            var current = RoofFootprintValidator.Validate(currentInput);
            var currentStored = RoofDefinitionStore.Read(owner);
            if (!current.IsValid || current.Footprint is null || currentStored.Data is null)
            {
                failureMessageKey = "Command_Roof_PersistSourceChanged";
                return null;
            }

            var data = RoofDefinitionPersistence.UpdateGeometry(
                currentStored.Data,
                currentInput,
                newGeometry);
            if (newGeometry is not HipRoofGeometry)
            {
                data = MonopitchRoofDefinitionRules
                    .PreserveGeneratedMemberOverridesAcrossSemanticMirror(
                        data,
                        selectionGeometry,
                        newGeometry);
            }
            var restored = RoofDefinitionPersistence.Restore(
                currentInput,
                current.Footprint,
                data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                failureMessageKey = "Command_Roof_PersistSourceChanged";
                return null;
            }

            if (newGeometry is not HipRoofGeometry &&
                !RoofAttachedManualLifecycleService.TryRebaseForMonopitchSemanticMirror(
                        document,
                        transaction,
                        owner,
                        selectionGeometry,
                        newGeometry,
                        currentStored.Data.Overrides))
            {
                failureMessageKey = "Command_RoofRafters_GenerationFailed";
                return null;
            }

            RoofDefinitionStore.Write(owner, transaction, data);

            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
            var edges = RoofWireframe.Create(restored.Geometry, sourceElevation);
            var signature = RoofWireframe.BuildGenerationSignature(edges);
            if (!RoofDisplayService.Rebuild(
                    document.Database,
                    transaction,
                    owner.ObjectId,
                    ownerReference,
                    edges,
                    signature))
            {
                failureMessageKey = "Command_Roof_DisplayFailed";
                return null;
            }

            // Only regenerate rafters when the physical geometry actually changed;
            // an unchanged edit stays handle-preserving (freshness check short-circuits).
            var geometryChanged = !string.Equals(
                newGeometry.Signature,
                selectionGeometry.Signature,
                StringComparison.Ordinal);
            var outcome = RoofGeneratedRafterSetService.ReplacementOutcome.NotApplicable;
            RoofGeneratedAnchorResolutionContext? anchorResolutionContext = null;
            if (restored.Geometry is not HipRoofGeometry)
            {
                outcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
                    document.Database,
                    transaction,
                    document.Editor,
                    owner,
                    restored.Geometry,
                    TimberElementDefaultProfileStore.Load(),
                    ElementLayerProfileStore.Load(),
                    out anchorResolutionContext,
                    forceRegenerateOnSourceResize: geometryChanged,
                    rebuildReason: "roof-edit");
                if (outcome == RoofGeneratedRafterSetService.ReplacementOutcome.Failed)
                {
                    failureMessageKey = "Command_RoofRafters_GenerationFailed";
                    return null;
                }
            }

            if (outcome == RoofGeneratedRafterSetService.ReplacementOutcome.Replaced)
            {
                var footprintVertices = current.Footprint.Vertices;
                _ = RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner(
                    document,
                    transaction,
                    ownerReference,
                    oldAnchorHandleByKey: null,
                    originFilter: RoofAttachedManualOrigin.Copy,
                    sourceFootprintVertices: footprintVertices,
                    anchorResolutionContext: anchorResolutionContext!);
                _ = RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner(
                    document,
                    transaction,
                    ownerReference,
                    oldAnchorHandleByKey: null,
                    originFilter: RoofAttachedManualOrigin.Split,
                    sourceFootprintVertices: footprintVertices,
                    anchorResolutionContext: anchorResolutionContext!);
            }

            RoofUnlockIndicatorService.Sync(document.Database, transaction, owner);
            RoofDisplayGroupSelectabilityService.ApplyForOwner(
                document.Database,
                transaction,
                ownerId);
            _ = RoofAssemblyGroupSyncService.TrySyncForOwner(
                document,
                transaction,
                ownerId);
            transaction.Commit();
            return outcome;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Soft outcomes still applied the definition + display update but did not
    /// regenerate the generated set; report honestly instead of claiming a full
    /// replacement. Mirrors the resize lifecycle's safe skip semantics.
    /// </summary>
    private static string GetSoftReplacementMessage(
        RoofGeneratedRafterSetService.ReplacementOutcome outcome) =>
        outcome switch
        {
            RoofGeneratedRafterSetService.ReplacementOutcome.SkippedAmbiguousRecipe =>
                "Command_RoofRafters_RecipeAmbiguous",
            RoofGeneratedRafterSetService.ReplacementOutcome.SkippedInvalidLayout or
            RoofGeneratedRafterSetService.ReplacementOutcome.Failed =>
                "Command_RoofRafters_GenerationFailed",
            _ => "Command_RoofEdit_UpdatedFormat",
        };

    private static void ShowPreview(
        Document document,
        IRoofGeometry geometry,
        double sourceElevation)
    {
        using (RoofTransientPreviewSession.Show(document, geometry, sourceElevation))
        {
            _ = document.Editor.GetString(new PromptStringOptions(
                UiStrings.GetString("Command_Roof_PreviewClosePrompt"))
            {
                AllowSpaces = false,
            });
        }
    }

    private static bool TryPromptOrientationDirection(
        Editor editor,
        RoofKind kind,
        out RoofDirection2D direction)
    {
        direction = default;

        var isMonopitch = kind == RoofKind.Monopitch;
        var directionStartPrompt = UiStrings.GetString(isMonopitch
            ? "Command_Roof_HighSidePointPrompt"
            : "Command_Roof_RidgeDirectionStartPrompt");
        if (isMonopitch)
        {
            directionStartPrompt = "\n" + directionStartPrompt;
        }
        var directionStartResult = editor.GetPoint(new PromptPointOptions(
            directionStartPrompt));
        if (directionStartResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var directionEndPrompt = UiStrings.GetString(isMonopitch
            ? "Command_Roof_LowSidePointPrompt"
            : "Command_Roof_RidgeDirectionEndPrompt");
        if (isMonopitch)
        {
            directionEndPrompt = "\n" + directionEndPrompt;
        }
        var directionEndOptions = new PromptPointOptions(
            directionEndPrompt)
        {
            BasePoint = directionStartResult.Value,
            UseBasePoint = true,
            UseDashedLine = true,
        };
        var directionEndResult = editor.GetPoint(directionEndOptions);
        if (directionEndResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var start = directionStartResult.Value;
        var end = directionEndResult.Value;
        if (!RoofDirection2D.TryCreate(end.X - start.X, end.Y - start.Y, out direction))
        {
            editor.WriteMessage(UiStrings.GetString("Command_Roof_GeometryErrorDirection"));
            return false;
        }
        if (isMonopitch)
        {
            direction = MonopitchRoofDirectionPresentationRules.ToCanonicalLowToHigh(
                direction);
        }

        return true;
    }

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch (System.Exception)
        {
            return IntPtr.Zero;
        }
    }
}
