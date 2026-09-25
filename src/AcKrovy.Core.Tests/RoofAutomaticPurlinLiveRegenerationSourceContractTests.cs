using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source contracts for headless automatic-purlin regeneration on supported roof
/// lifecycle changes (AK_ROOF_EDIT, STRETCH, GRIP_STRETCH).
/// </summary>
public sealed class RoofAutomaticPurlinLiveRegenerationSourceContractTests
{
    private static readonly string LiveService = Read(
        "Infrastructure", "RoofAutomaticPurlinLiveRegenerationService.cs");
    private static readonly string EditWorkflow = Read(
        "Infrastructure", "RoofEditCommandWorkflow.cs");
    private static readonly string LiveResize = Read(
        "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string EditState = Read(
        "Infrastructure", "RoofEditStateCommandWorkflow.cs");
    private static readonly string Materialization = Read(
        "Infrastructure", "RoofAutomaticPurlinMaterializationService.cs");
    private static readonly string Display = Read(
        "Infrastructure", "RoofDisplayService.cs");
    private static readonly string ManualEdit = Read(
        "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string PlanningResolver = Read(
        "Infrastructure", "RoofAutomaticPurlinPlanningRafterResolver.cs");

    [Fact]
    public void LiveService_SkipsWhenNoPersistedLayout()
    {
        Assert.Contains("RoofPurlinLayoutStore.Read(owner)", LiveService);
        Assert.Contains("Outcome.NotConfigured", LiveService);
        Assert.Contains("!storedLayout.Exists", LiveService);
        Assert.DoesNotContain("CreateNewDraftDefaults", LiveService);
        Assert.DoesNotContain("AutomaticPurlinDialogWindow", LiveService);
        Assert.DoesNotContain("ShowModalWindow", LiveService);
    }

    [Fact]
    public void LiveService_UsesPersistedLayoutDatumAndWallPlateFlag()
    {
        Assert.Contains("storedLayout.Data", LiveService);
        Assert.Contains("storedDatum.Data", LiveService);
        Assert.Contains("includeWallPlates: layoutForMaterialize.WallPlateEnabled", LiveService);
        Assert.Contains("MaterializeInTransaction(", LiveService);
        Assert.DoesNotContain("TryApplySelectedRafterDimensions", LiveService);
        Assert.DoesNotContain("authoritativeRafterHeightMm:", LiveService);
    }

    [Fact]
    public void LiveService_EmitsDebugDiagnosticWithoutEmptyApplyToken()
    {
        Assert.Contains("ROOF_PURLIN_LIVE_REGEN", LiveService);
        Assert.Contains("ROOF_PURLIN_PREFLIGHT", LiveService);
        Assert.Contains("trigger=", LiveService);
        Assert.Contains("layout=", LiveService);
        Assert.DoesNotContain("ROOF_PURLIN_APPLY", LiveService);
    }

    [Fact]
    public void LiveService_ExposesDryPlanPreflightAgainstProposedGeometry()
    {
        Assert.Contains("TryValidatePersistedLayoutForProposedGeometry(", LiveService);
        Assert.Contains("RoofAutomaticPurlinPlanner.Create(", LiveService);
        Assert.Contains("proposedGeometry", LiveService);
        Assert.Contains("PreflightOutcome.Invalid", LiveService);
        Assert.Contains("RoofAutomaticPurlinPitchAdaptationRules", LiveService);
        Assert.Contains("previousGeometry", LiveService);
        // Preflight remains write-free for definition/display; regen may persist
        // pitch-adapted BottomEdge placement values.
        Assert.DoesNotContain("RoofDefinitionStore.Write", LiveService);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", LiveService);
    }

    [Fact]
    public void LiveService_PersistsPitchAdaptedLayoutOnRegenerate()
    {
        Assert.Contains("TryAdaptLayoutPreservingPlanStations(", LiveService);
        Assert.Contains("RoofPurlinLayoutStore.Write(owner, transaction, layoutForMaterialize)", LiveService);
        Assert.Contains("adapted.LayoutChanged", LiveService);
    }

    [Fact]
    public void RoofEdit_RunsPurlinPreflightBeforeAnyDwgMutation()
    {
        Assert.Contains(
            "TryValidatePersistedLayoutForProposedGeometry(",
            EditWorkflow);
        var preflight = EditWorkflow.IndexOf(
            "TryValidatePersistedLayoutForProposedGeometry(",
            StringComparison.Ordinal);
        var write = EditWorkflow.IndexOf(
            "RoofDefinitionStore.Write(owner, transaction, data);",
            StringComparison.Ordinal);
        var rebuild = EditWorkflow.IndexOf(
            "RoofDisplayService.Rebuild(",
            StringComparison.Ordinal);
        Assert.True(preflight >= 0);
        Assert.True(write > preflight);
        Assert.True(rebuild > write);
        Assert.Contains("if (!purlinPreflight.IsSuccess)", EditWorkflow);
        Assert.Contains("WallPlatePlanDistanceBelowMinimum", LiveService);
        Assert.Contains("Command_RoofEdit_PurlinElevationOutsideRoof", LiveService);
        Assert.Contains("purlinPreflight.LocalizationKey", EditWorkflow);
        Assert.Contains("previousGeometry: previousHipForPurlins", EditWorkflow);
    }

    [Fact]
    public void RoofEdit_HooksAfterStructuralBeforeCommit_WithDeferredGroupSync()
    {
        Assert.Contains("RoofAutomaticPurlinLiveRegenerationService.TryRegenerateInTransaction(", EditWorkflow);
        Assert.Contains("trigger: \"RoofEdit\"", EditWorkflow);
        Assert.Contains("syncAssemblyGroup: false", EditWorkflow);
        var structural = EditWorkflow.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var purlin = EditWorkflow.IndexOf(
            "RoofAutomaticPurlinLiveRegenerationService.TryRegenerateInTransaction(",
            StringComparison.Ordinal);
        var finalSync = EditWorkflow.IndexOf(
            "RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        var commit = EditWorkflow.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(structural >= 0);
        Assert.True(purlin > structural);
        Assert.True(finalSync > purlin);
        Assert.True(commit > finalSync);
        Assert.Contains("if (!purlinLive.IsSuccess)", EditWorkflow);
        Assert.Contains("if (geometryChanged)", EditWorkflow);
    }

    [Fact]
    public void LiveResize_HooksPreflightAndDeferredGroupSync()
    {
        Assert.Contains("TryValidatePersistedLayoutForProposedGeometry(", LiveResize);
        Assert.Contains("RoofAutomaticPurlinLiveRegenerationService.TryRegenerateInTransaction(", LiveResize);
        Assert.Contains("ResolvePurlinLiveTrigger(", LiveResize);
        Assert.Contains("\"GripStretch\"", LiveResize);
        Assert.Contains("\"Stretch\"", LiveResize);
        Assert.Contains("ResizeApplyResult.HardFailure", LiveResize);
        Assert.Contains("syncAssemblyGroup: false", LiveResize);
        var preflight = LiveResize.IndexOf(
            "TryValidatePersistedLayoutForProposedGeometry(",
            StringComparison.Ordinal);
        var write = LiveResize.IndexOf(
            "RoofDefinitionStore.Write(owner, transaction, updated);",
            StringComparison.Ordinal);
        var structural = LiveResize.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var purlin = LiveResize.IndexOf(
            "RoofAutomaticPurlinLiveRegenerationService.TryRegenerateInTransaction(",
            StringComparison.Ordinal);
        var finalSync = LiveResize.LastIndexOf(
            "RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        Assert.True(preflight >= 0);
        Assert.True(write > preflight);
        Assert.True(structural > write);
        Assert.True(purlin > structural);
        Assert.True(finalSync > purlin);
    }

    [Fact]
    public void EditStateRebuild_AlsoRegeneratesPersistedPurlins()
    {
        Assert.Contains("RoofAutomaticPurlinLiveRegenerationService.TryRegenerateInTransaction(", EditState);
        Assert.Contains("trigger: \"EditState\"", EditState);
    }

    [Fact]
    public void MaterializeAndRebuild_SupportDeferredAssemblyGroupSync()
    {
        Assert.Contains("bool syncAssemblyGroup = true", Materialization);
        Assert.Contains("if (syncAssemblyGroup)", Materialization);
        Assert.Contains("bool syncAssemblyGroup = true", Display);
        Assert.Contains("if (syncAssemblyGroup)", Display);
        Assert.Contains(
            "includeWallPlates: storedLayout.Data.WallPlateEnabled",
            Materialization);
        Assert.Contains("RoofAutomaticPurlinPlanningRafterResolver.TryResolve(", Materialization);
        Assert.DoesNotContain("RoofAutomaticPurlinRafterDimensionsResolver.TryResolveForOwner(", Materialization);
        Assert.Contains("rafter-source-conflict", PlanningResolver);
        Assert.Contains("rafter-source-missing-actual", PlanningResolver);
        Assert.Contains("MissingActualWithRecoverableManual", PlanningResolver);
        Assert.Contains("RoofAutomaticPurlinPlanningRafterResolver.TryResolve(", LiveService);
        Assert.Contains("TrySyncForOwner(", Materialization);
    }

    [Fact]
    public void AttachedManualLifecycle_NotRoutedThroughPurlinLiveService()
    {
        Assert.DoesNotContain("RoofAutomaticPurlinLiveRegenerationService", ManualEdit);
        Assert.DoesNotContain("ROOF_PURLIN_LIVE_REGEN", ManualEdit);
    }

    [Fact]
    public void FailureIsAtomic_CallerAbortsWithoutPartialCommit()
    {
        Assert.Contains("if (!purlinLive.IsSuccess)", EditWorkflow);
        Assert.Contains("return null;", Segment(EditWorkflow, "if (!purlinLive.IsSuccess)", "RoofUnlockIndicatorService"));
        Assert.Contains(
            "return ResizeApplyResult.HardFailure;",
            Segment(LiveResize, "if (!purlinLive.IsSuccess)", "RoofUnlockIndicatorService"));
        Assert.Contains("if (!purlinPreflight.IsSuccess)", EditWorkflow);
        Assert.Contains("if (!purlinPreflight.IsSuccess)", LiveResize);
        var preflightAbort = Segment(
            EditWorkflow,
            "if (!purlinPreflight.IsSuccess)",
            "RoofDefinitionStore.Write");
        Assert.Contains("return null;", preflightAbort);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", preflightAbort);
    }

    [Fact]
    public void PurlinFailureUsesPurlinSpecificMessageNotRafterGenerationFailed()
    {
        Assert.Contains("LocalizationKeyElevationOutside", LiveService);
        Assert.Contains("LocalizationKeyLayoutIncompatible", LiveService);
        Assert.Contains(
            "RoofAutomaticPurlinLiveRegenerationService.LocalizationKeyLayoutIncompatible",
            EditWorkflow);
        Assert.Contains(
            "RoofAutomaticPurlinLiveRegenerationService.LocalizationKeyLayoutIncompatible",
            LiveResize);
        var preflightFail = Segment(
            EditWorkflow,
            "if (!purlinPreflight.IsSuccess)",
            "if (newGeometry is not HipRoofGeometry");
        Assert.Contains("purlinPreflight.LocalizationKey", preflightFail);
        Assert.DoesNotContain("Command_RoofRafters_GenerationFailed", preflightFail);
        var liveFail = Segment(
            EditWorkflow,
            "if (!purlinLive.IsSuccess)",
            "RoofUnlockIndicatorService");
        Assert.DoesNotContain("Command_RoofRafters_GenerationFailed", liveFail);
    }

    [Fact]
    public void RoofEdit_KeepsDialogOpenOnRecoverablePurlinPreflightFailure()
    {
        var hipEdit = Segment(
            EditWorkflow,
            "private static void RunHipEditDialog(",
            "private static bool IsRecoverablePurlinEditValidation(");
        Assert.Contains("IsRecoverablePurlinEditValidation(failureMessageKey)", hipEdit);
        Assert.Contains("viewModel.SetSessionValidation(failureMessageKey)", hipEdit);
        Assert.Contains("dialog.FocusSlopeInput()", hipEdit);
        Assert.Contains("continue;", hipEdit);
        Assert.DoesNotContain("ShowModalWindow", Segment(
            hipEdit,
            "IsRecoverablePurlinEditValidation(failureMessageKey)",
            "continue;"));
        Assert.DoesNotContain("TransientNotificationService", hipEdit);
        Assert.DoesNotContain("MessageBox", hipEdit);

        // STRETCH / GRIP keep the separate editor/warning path; no edit dialog reopen.
        Assert.DoesNotContain("SetSessionValidation(", LiveResize);
        Assert.DoesNotContain("RunHipEditDialog(", LiveResize);
        Assert.DoesNotContain("HipRoofPreviewWindow", LiveResize);
        Assert.Contains("UiStrings.GetString(", Segment(
            LiveResize,
            "if (result == ResizeApplyResult.HardFailure)",
            "return;"));
    }

    [Fact]
    public void RoofEditPurlinWarningKeys_AreLocalizedResourcesNotBareTokens()
    {
        var defaultResx = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.Localization",
            "Resources",
            "UiStrings.resx"));
        Assert.Contains("Command_RoofEdit_PurlinElevationOutsideRoof", defaultResx);
        Assert.Contains("Command_RoofEdit_PurlinLayoutIncompatible", defaultResx);
        Assert.Contains("Medziľahlá väznica", defaultResx);
        Assert.Contains("Uloženie väzníc", defaultResx);
        Assert.DoesNotContain(
            "name=\"Command_RoofEdit_PurlinElevationOutsideRoof\" xml:space=\"preserve\"><value>Command_RoofEdit_PurlinElevationOutsideRoof</value>",
            defaultResx);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, start);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, end);
        return source[startIndex..endIndex];
    }

    private static string Read(string folder, string fileName) =>
        RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", folder, fileName);
}
