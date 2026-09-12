using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Explicit Stage 6 source-only intelligent rafter generation workflow.</summary>
internal static class RoofRafterCommandWorkflow
{
    public static void Run(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var editor = document.Editor;
        if (!TrySelectCurrentRoof(document, out var selectedRoof))
        {
            return;
        }

        if (selectedRoof.ExistingGeneratedRafterCount > 0)
        {
            editor.WriteMessage(UiStrings.Format(
                UiStrings.GetString("Command_RoofRafters_ExistingFoundFormat"),
                selectedRoof.ExistingGeneratedRafterCount));
            if (selectedRoof.GeneratedSetIsStale)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_ExistingStale"));
            }
            editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_ReplacementDeferred"));
            return;
        }

        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var canonicalRafterDefaults = TimberElementDefaults.For(
            TimberElementType.Rafter,
            defaultProfile);
        var uiPreferences = SettingsUiPreferencesStore.Load();
        var remembered = uiPreferences.AutomaticRafterPreferences ??
            RoofRafterPreferences.CreateFirstUse(canonicalRafterDefaults.Material);
        var rafterSettings = AutoCadRoofRafterSpacingStore.ReadEffective(
            document.Database,
            out _);
        var workingPreferences = remembered with
        {
            MaximumSpacingMm = rafterSettings.DefaultAutomaticSpacingMm,
        };
        var dialog = new RoofRafterWindow(
            selectedRoof.Geometry,
            workingPreferences,
            rafterSettings.MinimumAutomaticSpacingMm,
            uiPreferences.Theme);
        SettingsWindowOwner.TryAssign(dialog, TryGetAutoCadMainWindowHandle());
        using var preview = new RoofRafterTransientPreviewController(
            document,
            selectedRoof.SourceElevation);
        using var hipPreview = new RoofFaceRafterTransientPreviewController(
            document,
            selectedRoof.SourceElevation);
        Action<RoofRafterLayout?> previewChanged = preview.Refresh;
        Action<RoofFaceRafterLayout?> hipPreviewChanged = hipPreview.Refresh;
        dialog.PreviewLayoutChanged += previewChanged;
        dialog.HipPreviewLayoutChanged += hipPreviewChanged;
        var accepted = false;
        try
        {
            preview.Refresh(dialog.PreviewLayout);
            hipPreview.Refresh(dialog.HipPreviewLayout);
            accepted = AcApp.ShowModalWindow(dialog) == true && dialog.Request is not null;
        }
        finally
        {
            dialog.PreviewLayoutChanged -= previewChanged;
            dialog.HipPreviewLayoutChanged -= hipPreviewChanged;
            preview.Refresh(null);
            hipPreview.Refresh(null);
        }
        if (!accepted || dialog.Request is null)
        {
            return;
        }

        var result = TryCreateRafters(
            document,
            selectedRoof.OwnerId,
            selectedRoof.OwnerReference,
            selectedRoof.Geometry.Kind,
            dialog.Request,
            rafterSettings.MinimumAutomaticSpacingMm,
            defaultProfile);
        if (result.IsSuccess)
        {
            SettingsUiPreferencesStore.Save(uiPreferences with
            {
                AutomaticRafterPreferences = dialog.Request.ToPreferences(),
            });
        }
        editor.WriteMessage(result.IsSuccess
            ? UiStrings.Format(
                UiStrings.GetString("Command_RoofRafters_CreatedFormat"),
                result.CreatedCount)
            : UiStrings.GetString(result.FailureMessageKey));
    }

    private static bool TrySelectCurrentRoof(
        Document document,
        out SelectedRoof selectedRoof)
    {
        selectedRoof = default!;
        var editor = document.Editor;
        while (true)
        {
            var selected = editor.GetEntity(new PromptEntityOptions(
                UiStrings.GetString("Command_RoofRafters_SelectPrompt")));
            if (selected.Status != PromptStatus.OK)
            {
                return false;
            }

            using var transaction = document.Database.TransactionManager.StartTransaction();
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
                    editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                }
                continue;
            }

            if (transaction.GetObject(resolution.OwnerId, OpenMode.ForRead) is not Polyline owner)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                continue;
            }

            var sourceInput = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(sourceInput);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                continue;
            }

            var restored = RoofDefinitionPersistence.Restore(
                sourceInput,
                validation.Footprint,
                stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                editor.WriteMessage(UiStrings.GetString(
                    restored.Error == RoofDefinitionRestoreError.StaleFootprint
                        ? "Command_Roof_PersistedStale"
                        : "Command_RoofRafters_InvalidRoof"));
                return false;
            }
            if (restored.Geometry is not SimpleGableRoofGeometry and
                not MonopitchRoofGeometry and
                not HipRoofGeometry)
            {
                editor.WriteMessage(UiStrings.GetString("Command_RoofRafters_InvalidRoof"));
                return false;
            }

            var ownerReference = owner.Handle.ToString();
            var generatedIds = RoofGeneratedTimberStore.FindByOwner(
                document.Database,
                transaction,
                ownerReference);
            selectedRoof = new SelectedRoof(
                resolution.OwnerId,
                ownerReference,
                RoofPolylineExtractor.GetSourceElevation(owner),
                restored.Geometry,
                generatedIds.Count,
                IsGeneratedSetStale(
                    document.Database,
                    transaction,
                    generatedIds,
                    restored.Geometry));
            return true;
        }
    }

    private static RoofRafterCreationResult TryCreateRafters(
        Document document,
        ObjectId ownerId,
        string expectedOwnerReference,
        RoofKind expectedRoofKind,
        RoofRafterCreationRequest request,
        double minimumAutomaticSpacingMm,
        TimberElementDefaultProfile defaultProfile)
    {
        var phase = "start";
        try
        {
            phase = "ElementLayerProfileStore.Load";
            var layerProfile = ElementLayerProfileStore.Load();
            phase = "Document.LockDocument";
            using var documentLock = document.LockDocument();
            phase = "Transaction.StartTransaction";
            using var transaction = document.Database.TransactionManager.StartTransaction();
            phase = "Owner.OpenAndValidate";
            if (transaction.GetObject(ownerId, OpenMode.ForRead) is not Polyline owner ||
                !string.Equals(
                    owner.Handle.ToString(),
                    expectedOwnerReference,
                    StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "Command_RoofRafters_SourceChanged");
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }

            phase = "RoofDefinitionPersistence.Restore";
            var sourceInput = RoofPolylineExtractor.Extract(owner);
            var validation = RoofFootprintValidator.Validate(sourceInput);
            var stored = RoofDefinitionStore.Read(owner);
            if (!validation.IsValid || validation.Footprint is null || stored.Data is null)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "Command_RoofRafters_SourceChanged");
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }

            var restored = RoofDefinitionPersistence.Restore(
                sourceInput,
                validation.Footprint,
                stored.Data);
            if (!restored.IsValid || restored.Geometry is null)
            {
                var errorKey = restored.Error == RoofDefinitionRestoreError.StaleFootprint
                    ? "Command_Roof_PersistedStale"
                    : "Command_RoofRafters_SourceChanged";
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    errorKey);
#endif
                return RoofRafterCreationResult.Failure(errorKey);
            }
            if (restored.Geometry.Kind != expectedRoofKind)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "Command_RoofRafters_SourceChanged");
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }
            if (restored.Geometry is not SimpleGableRoofGeometry and
                not MonopitchRoofGeometry and
                not HipRoofGeometry)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "Command_RoofRafters_SourceChanged");
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_SourceChanged");
            }
            phase = "RoofGeneratedTimberStore.FindByOwner";
            if (RoofGeneratedTimberStore.FindByOwner(
                    document.Database,
                    transaction,
                    expectedOwnerReference).Count > 0)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "Command_RoofRafters_ReplacementDeferred");
#endif
                return RoofRafterCreationResult.Failure(
                    "Command_RoofRafters_ReplacementDeferred");
            }

            phase = "RoofRafterRequestValidator.ValidateAutomaticInputs";
            var inputError = RoofRafterRequestValidator.ValidateAutomaticInputs(
                request.WidthMm,
                request.HeightMm,
                request.MaximumSpacingMm,
                minimumAutomaticSpacingMm,
                request.Material);
            if (inputError != RoofRafterRequestValidationError.None)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    inputError.ToString());
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_InvalidSpacing");
            }

            phase = restored.Geometry is HipRoofGeometry
                ? "RoofRafterRequestValidator.ValidateHip"
                : "RoofRafterRequestValidator.Validate";
            var currentValidation = restored.Geometry is HipRoofGeometry hipGeometry
                ? RoofRafterRequestValidator.ValidateHip(
                    hipGeometry,
                    request.WidthMm,
                    request.HeightMm,
                    request.MaximumSpacingMm,
                    request.Material.Trim())
                : RoofRafterRequestValidator.Validate(
                    restored.Geometry,
                    request.WidthMm,
                    request.HeightMm,
                    request.MaximumSpacingMm,
                    minimumAutomaticSpacingMm,
                    request.Material);
            if (!currentValidation.IsValid || currentValidation.Layout is null)
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    currentValidation.Error.ToString());
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_InvalidSpacing");
            }

            phase = "TimberMaterialCatalog.TryGetItem";
            if (!TimberMaterialCatalog.TryGetItem(request.Material, out _))
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    RoofRafterRequestValidationError.InvalidMaterial.ToString());
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_InvalidSpacing");
            }

            phase = "RoofRafterMaterializationRules.IsConsistent";
            if (!RoofRafterMaterializationRules.IsConsistent(
                    restored.Geometry,
                    currentValidation.Layout))
            {
#if DEBUG
                RoofRafterPermanentCreateDiag.WriteCreateResultFailure(
                    document.Editor,
                    expectedOwnerReference,
                    phase,
                    "false");
#endif
                return RoofRafterCreationResult.Failure("Command_RoofRafters_InvalidSpacing");
            }

#if DEBUG
            RoofRafterPermanentCreateDiag.WriteRequest(
                document.Editor,
                expectedOwnerReference,
                restored.Geometry.Kind,
                request,
                currentValidation.Layout);
            RoofRafterPermanentCreateDiag.WriteLayoutSummary(
                document.Editor,
                restored.Geometry,
                currentValidation.Layout);
            if (restored.Geometry is HipRoofGeometry hipForCoverageDiag)
            {
                var createInput = RoofPolylineExtractor.Extract(owner);
                RoofRafterPermanentCreateDiag.WriteHipSourcePolygon(
                    document.Editor,
                    expectedOwnerReference,
                    createInput);
                RoofRafterPermanentCreateDiag.WriteSolveInput(
                    document.Editor,
                    expectedOwnerReference,
                    createInput,
                    hipForCoverageDiag,
                    request.MaximumSpacingMm,
                    currentValidation.Layout);
                RoofRafterPermanentCreateDiag.WriteFaceCoverageSummary(
                    document.Editor,
                    hipForCoverageDiag,
                    currentValidation.Layout);
            }
#endif
            phase = "RoofGeneratedRafterSetService.Materialize";
            var created = RoofGeneratedRafterSetService.Materialize(
                document.Database,
                transaction,
                document.Editor,
                owner,
                expectedOwnerReference,
                restored.Geometry,
                currentValidation.Layout,
                new RoofRafterGenerationRecipe(
                    request.WidthMm,
                    request.HeightMm,
                    request.MaximumSpacingMm,
                    request.Material),
                defaultProfile,
                layerProfile);
            var automaticStructuralRafterCount = 0;
            if (restored.Geometry is HipRoofGeometry automaticRafterHipGeometry)
            {
                phase = "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction";
                var structuralRafters =
                    RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(
                        document,
                        transaction,
                        owner,
                        expectedOwnerReference,
                        sourceInput,
                        automaticRafterHipGeometry,
                        defaultProfile,
                        layerProfile);
                if (!structuralRafters.IsSuccess)
                {
                    throw new InvalidOperationException(
                        "Automatic Hip/Valley rafter materialization failed: " +
                        structuralRafters.Result);
                }

                automaticStructuralRafterCount = structuralRafters.Actual;
            }
#if DEBUG
            var annotationCount = RoofRafterPermanentCreateDiag.CountAnnotations(
                document.Database,
                transaction,
                created.Keys.ToArray());
#endif
            phase = "Transaction.Commit";
            transaction.Commit();
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteSuccess(
                document.Editor,
                expectedOwnerReference,
                created.Count,
                annotationCount);
#endif
            return RoofRafterCreationResult.Success(
                currentValidation.Layout.Rafters.Count + automaticStructuralRafterCount);
        }
        catch (RoofRafterMaterializationPhaseException ex)
        {
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteCreateFailure(
                document.Editor,
                expectedOwnerReference,
                ex.ServicePhase,
                ex.CandidateOrdinal,
                ex.InnerException ?? ex);
#else
            _ = ex;
#endif
            return RoofRafterCreationResult.Failure("Command_RoofRafters_GenerationFailed");
        }
        catch (System.Exception ex)
        {
#if DEBUG
            RoofRafterPermanentCreateDiag.WriteCreateFailure(
                document.Editor,
                expectedOwnerReference,
                phase,
                -1,
                ex);
#else
            _ = ex;
#endif
            return RoofRafterCreationResult.Failure("Command_RoofRafters_GenerationFailed");
        }
    }

    private static bool IsGeneratedSetStale(
        Database database,
        Transaction transaction,
        IReadOnlyList<ObjectId> generatedIds,
        IRoofGeometry geometry) =>
        RoofGeneratedRafterSetService.IsGeneratedSetStale(
            database,
            transaction,
            generatedIds,
            geometry);

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

    private sealed record SelectedRoof(
        ObjectId OwnerId,
        string OwnerReference,
        double SourceElevation,
        IRoofGeometry Geometry,
        int ExistingGeneratedRafterCount,
        bool GeneratedSetIsStale);

    private sealed record RoofRafterCreationResult(
        bool IsSuccess,
        int CreatedCount,
        string FailureMessageKey)
    {
        public static RoofRafterCreationResult Success(int createdCount) =>
            new(true, createdCount, string.Empty);

        public static RoofRafterCreationResult Failure(string messageKey) =>
            new(false, 0, messageKey);
    }
}
