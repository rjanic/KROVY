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
/// AK_ROOF_EDIT: edits an already-created SimpleGable / AsymmetricGable roof through
/// the shared GableRoofGeometryWindow. The dialog is seeded from the persisted
/// definition (kind, α, β, ΔH and the PERSISTED ridge direction — never the
/// footprint fallback). Everything before Apply is read-only: transient preview
/// only, no definition write, no display/rafter/annotation/group mutation.
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
            SimpleGableRoofGeometry restoredGeometry;
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
        SimpleGableRoofGeometry restoredGeometry)
    {
        var footprint = validation.Footprint!;
        var viewModel = new GableRoofGeometryViewModel(footprint, storedDefinition.Kind);
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
                        if (TryPromptRidgeDirection(document.Editor, out var direction))
                        {
                            viewModel.SetRidgeDirection(direction);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Preview:
                        if (viewModel.TryGetGeometry(out var previewGeometry) &&
                            previewGeometry is not null)
                        {
                            document.Editor.SetImpliedSelection([ownerId]);
                            document.Editor.UpdateScreen();
                            ShowPreview(document, previewGeometry, sourceElevation);
                        }
                        continue;

                    case GableRoofGeometryDialogAction.Apply:
                        if (!viewModel.TryGetGeometry(out var geometry) || geometry is null)
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
    /// Applies the edited geometry to the EXISTING roof. Re-validates the source,
    /// rebases the persisted definition (schema 5), rebuilds the permanent display,
    /// regenerates the generated rafter set through the proven replacement path and
    /// replays anchored AttachedManual children against their rebuilt anchors.
    /// A single write transaction; no lock/transaction is held while the dialog is
    /// open or while the transient preview is active.
    /// </summary>
    private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(
        Document document,
        ObjectId ownerId,
        string ownerReference,
        SimpleGableRoofGeometry selectionGeometry,
        SimpleGableRoofGeometry newGeometry,
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
            var restored = RoofDefinitionPersistence.Restore(
                currentInput,
                current.Footprint,
                data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                failureMessageKey = "Command_Roof_PersistSourceChanged";
                return null;
            }

            RoofDefinitionStore.Write(owner, transaction, data);

            var sourceElevation = RoofPolylineExtractor.GetSourceElevation(owner);
            var edges = SimpleGableRoofWireframe.Create(restored.Geometry, sourceElevation);
            var signature = SimpleGableRoofWireframe.BuildGenerationSignature(edges);
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
            var outcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(
                document.Database,
                transaction,
                document.Editor,
                owner,
                restored.Geometry,
                TimberElementDefaultProfileStore.Load(),
                ElementLayerProfileStore.Load(),
                out var anchorResolutionContext,
                forceRegenerateOnSourceResize: geometryChanged);
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
                    anchorResolutionContext: anchorResolutionContext);
                _ = RoofAttachedManualLifecycleService.ReplayAnchoredChildrenForOwner(
                    document,
                    transaction,
                    ownerReference,
                    oldAnchorHandleByKey: null,
                    originFilter: RoofAttachedManualOrigin.Split,
                    sourceFootprintVertices: footprintVertices,
                    anchorResolutionContext: anchorResolutionContext);
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
        SimpleGableRoofGeometry geometry,
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

    private static bool TryPromptRidgeDirection(
        Editor editor,
        out RoofDirection2D direction)
    {
        direction = default;

        var directionStartResult = editor.GetPoint(new PromptPointOptions(
            UiStrings.GetString("Command_Roof_RidgeDirectionStartPrompt")));
        if (directionStartResult.Status != PromptStatus.OK)
        {
            return false;
        }

        var directionEndOptions = new PromptPointOptions(
            UiStrings.GetString("Command_Roof_RidgeDirectionEndPrompt"))
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
