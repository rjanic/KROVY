using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract guards for AK_ROOF_EDIT: the dedicated command routes to the
/// edit workflow, the shared GableRoofGeometryWindow/ViewModel are reused with a
/// seed-from-existing-geometry path, the read-only phase (open/preview/cancel)
/// never writes, and Apply replays the canonical rebuild pipeline (definition
/// rebase, display rebuild, ordinary/structural generated-set reconciliation,
/// anchored AttachedManual replay, indicator/selectability/group sync and final
/// draw-order restoration) without the create conflict path.
/// </summary>
public sealed class RoofEditCommandSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Commands = Read("src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs");
    private static readonly string Catalog = Read("src/AcKrovy.Localization/CommandUiCatalog.cs");
    private static readonly string EditWorkflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
    private static readonly string CreateWorkflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
    private static readonly string ViewModel = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryViewModel.cs");
    private static readonly string HipViewModel = Read("src/AcKrovy.AutoCAD/UI/HipRoofPreviewViewModel.cs");
    private static readonly string Window = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryWindow.xaml");

    [Fact]
    public void DedicatedCommand_RoutesToTheEditWorkflow()
    {
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.RoofEdit, CommandFlags.Modal | CommandFlags.Redraw)]", Commands);
        Assert.Contains("RoofEditCommandWorkflow.Run(ActiveDocument())", Commands);
        Assert.Contains("RoofEdit = \"AK_ROOF_EDIT\"", Catalog);
        Assert.Contains("RoofEdit,", Catalog);
    }

    [Fact]
    public void EditDoesNotOverloadAkEditOrTheCreateWindowArchitecture()
    {
        var editMethod = Segment(
            Commands,
            "public void Edit()",
            "public void FlipSlopeDirection()");
        Assert.DoesNotContain("RoofEditCommandWorkflow", editMethod);
        Assert.DoesNotContain("GableRoofGeometryWindow", editMethod);
        Assert.Equal(1, Count(CreateWorkflow, "new GableRoofGeometryWindow("));
        Assert.Contains("new GableRoofGeometryWindow(", EditWorkflow);
        Assert.Contains("viewModel.SeedFromExistingGeometry(restoredGeometry)", EditWorkflow);
        Assert.Contains("public void SeedFromExistingGeometry(SimpleGableRoofGeometry geometry)", ViewModel);
    }

    [Fact]
    public void SharedWindow_UsesCreateOrEditActionTextFromExplicitWorkflowContext()
    {
        Assert.DoesNotContain("isEditMode: true", CreateWorkflow);
        Assert.Contains("isEditMode: true", EditWorkflow);
        Assert.Contains("_isEditMode ? \"EditWindow_Apply\" : \"RoofGeometryWindow_Create\"", ViewModel);
        Assert.Contains("Content=\"{Binding PrimaryActionText}\"", Window);
    }

    [Fact]
    public void SelectionResolvesTheRoofOwnerThroughTheSharedResolver()
    {
        var selectionPath = Segment(
            EditWorkflow,
            "public static void Run(Document document)",
            "RunEditDialog(");
        Assert.Contains("RoofOwnerSelectionResolver.Resolve", selectionPath);
        Assert.Contains("RoofDefinitionStore.Read(polyline)", selectionPath);
        Assert.Contains("RoofDefinitionPersistence.Restore", selectionPath);
        Assert.Contains("OpenMode.ForRead", selectionPath);
        Assert.DoesNotContain("OpenMode.ForWrite", selectionPath);
        Assert.DoesNotContain("transaction.Commit", selectionPath);
    }

    [Fact]
    public void PreviewIsTransientOnlyAndNeverPersists()
    {
        var dialogPath = Segment(
            EditWorkflow,
            "private static void RunEditDialog(",
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(");
        var preview = Segment(
            dialogPath,
            "case GableRoofGeometryDialogAction.Preview:",
            "case GableRoofGeometryDialogAction.Apply:");
        Assert.Contains("ShowPreview(document, previewGeometry, sourceElevation)", preview);
        Assert.DoesNotContain("RoofDefinitionStore.Write", preview);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", preview);
        Assert.DoesNotContain("TryReplaceForSupportedResize", preview);
        Assert.DoesNotContain("ReplayAnchoredChildrenForOwner", preview);
        Assert.DoesNotContain("OpenMode.ForWrite", preview);
        Assert.DoesNotContain("transaction.Commit", preview);
    }

    [Fact]
    public void ApplyRebasesTheExistingDefinitionThroughTheCanonicalRebuildPipeline()
    {
        Assert.Contains("RoofDefinitionPersistence.UpdateGeometry(", EditWorkflow);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, data)", EditWorkflow);
        Assert.Contains("RoofDisplayService.Rebuild(", EditWorkflow);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", EditWorkflow);
        Assert.Contains("forceRegenerateOnSourceResize: geometryChanged", EditWorkflow);
        Assert.Contains("ReplayAnchoredChildrenForOwner(", EditWorkflow);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Copy", EditWorkflow);
        Assert.Contains("originFilter: RoofAttachedManualOrigin.Split", EditWorkflow);
        Assert.Contains("RoofUnlockIndicatorService.Sync(", EditWorkflow);
        Assert.Contains("RoofDisplayGroupSelectabilityService.ApplyForOwner(", EditWorkflow);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner(", EditWorkflow);
        Assert.Equal(1, Count(Segment(
            EditWorkflow,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(",
            "private static string GetSoftReplacementMessage("),
            "transaction.Commit();"));
    }

    [Fact]
    public void ApplyNeverUsesTheCreateOnlyPersistConflictPath()
    {
        Assert.DoesNotContain("Command_Roof_PersistConflict", EditWorkflow);
        Assert.DoesNotContain("if (RoofDefinitionStore.Read(owner).Exists)", EditWorkflow);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, data)", EditWorkflow);
    }

    [Fact]
    public void AnchoredReplayOnlyRunsAfterTheGeneratedSetWasReplaced()
    {
        var applyPath = Segment(
            EditWorkflow,
            "var outcome = RoofGeneratedRafterSetService.ReplacementOutcome.NotApplicable;",
            "RoofUnlockIndicatorService.Sync(");
        var replacedIndex = applyPath.IndexOf(
            "outcome == RoofGeneratedRafterSetService.ReplacementOutcome.Replaced",
            StringComparison.Ordinal);
        Assert.True(replacedIndex >= 0, "Replay must be gated on the Replaced outcome.");
        Assert.Contains("RoofAttachedManualOrigin.Copy", applyPath[replacedIndex..]);
        Assert.Contains("RoofAttachedManualOrigin.Split", applyPath[replacedIndex..]);
        Assert.Contains("sourceFootprintVertices: footprintVertices", applyPath);
    }

    [Fact]
    public void SeedRoundTripsThePersistedValuesAndNeverFallsBack()
    {
        Assert.Contains("FormatSeedSlope(geometry.Face0SlopeDegrees)", ViewModel);
        Assert.Contains("FormatSeedSlope(geometry.Face1SlopeDegrees)", ViewModel);
        Assert.Contains("Math.Round(geometry.EaveHeightDifferenceMm)", ViewModel);
        Assert.Contains("_ridgeDirection = geometry.RidgeDirection", ViewModel);
        Assert.Contains("_isAsymmetryMirrored = false", ViewModel);
        Assert.Contains("degrees.ToString(\"R\", CultureInfo.InvariantCulture)", ViewModel);
    }

    [Fact]
    public void PersistedHipDispatchesToHipEditBeforeTheGableEditorSeed()
    {
        var dispatch = Segment(
            EditWorkflow,
            "private static void RunEditDialog(",
            "private static void RunHipEditDialog(");
        var hipIndex = dispatch.IndexOf(
            "storedDefinition.Kind == RoofKind.Hip",
            StringComparison.Ordinal);
        var gableIndex = dispatch.IndexOf(
            "new GableRoofGeometryViewModel(",
            StringComparison.Ordinal);

        Assert.True(hipIndex >= 0);
        Assert.True(gableIndex > hipIndex);
        Assert.Contains("restoredGeometry is HipRoofGeometry hipGeometry", dispatch);
        Assert.Contains("RunHipEditDialog(", dispatch);
        Assert.Contains("return;", dispatch[hipIndex..gableIndex]);
    }

    [Fact]
    public void HipEditSeedsSavedSlopeAndAppliesWithoutDirectionControls()
    {
        var edit = Segment(
            EditWorkflow,
            "private static void RunHipEditDialog(",
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(");

        Assert.Contains("restoredGeometry.PrimarySlopeDegrees", edit);
        Assert.Contains("HipRoofDialogMode.Edit", edit);
        Assert.Contains("HipRoofPreviewDialogAction.Preview", edit);
        Assert.Contains("HipRoofPreviewDialogAction.Apply", edit);
        Assert.Contains("TryApply(", edit);
        Assert.DoesNotContain("PickRidgeDirection", edit);
        Assert.DoesNotContain("GableRoofGeometryViewModel", edit);
        Assert.DoesNotContain("RoofDefinitionStore.Write", edit);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", edit);
        Assert.DoesNotContain("transaction.Commit", edit);
        Assert.Contains("HipRoofDialogMode.Edit ? \"EditWindow_Apply\"", HipViewModel);
        Assert.Contains("CanApply => _geometry is not null", HipViewModel);
    }

    [Fact]
    public void HipPreviewIsTransientOnlyAndNeverEntersTheApplyPipeline()
    {
        var edit = Segment(
            EditWorkflow,
            "private static void RunHipEditDialog(",
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(");
        var preview = Segment(
            edit,
            "case HipRoofPreviewDialogAction.Preview:",
            "case HipRoofPreviewDialogAction.Apply:");

        Assert.Contains("ShowPreview(", preview);
        Assert.DoesNotContain("RoofFaceRafterLayoutService", preview);
        Assert.DoesNotContain("AutoCadRoofRafterSpacingStore", preview);
        Assert.DoesNotContain("TryApply(", preview);
        Assert.DoesNotContain("RoofDefinitionStore.Write", preview);
        Assert.DoesNotContain("RoofDisplayService.Rebuild", preview);
        Assert.DoesNotContain("OpenMode.ForWrite", preview);
        Assert.DoesNotContain("transaction.Commit", preview);
    }

    [Fact]
    public void HipApplyUsesTheSharedAtomicDefinitionDisplayRafterReplacementAndGroupPipeline()
    {
        var apply = Segment(
            EditWorkflow,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(",
            "private static string GetSoftReplacementMessage(");

        Assert.Contains("RoofDefinitionPersistence.UpdateGeometry(", apply);
        Assert.Contains("RoofDefinitionStore.Write(owner, transaction, data)", apply);
        Assert.Contains("RoofDisplayService.Rebuild(", apply);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner(", apply);
        Assert.Contains("RoofStructuralGeneratedStore.FindByOwner(", apply);
        Assert.Contains("RoofGeneratedRafterSetService.TryReplaceForSupportedResize(", apply);
        Assert.Contains("rebuildReason: \"roof-edit\"", apply);
        Assert.DoesNotContain("restored.Geometry is not HipRoofGeometry", apply);
        Assert.Contains(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            apply);
        Assert.Contains("hadGeneratedRafterSystem", apply);
        Assert.Contains("if (!structural.IsSuccess)", apply);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner(", apply);
        Assert.Contains("RoofDisplayService.EnsureAllDisplayBehindTimber(", apply);
        Assert.Equal(1, Count(apply, "RoofDisplayService.Rebuild("));
        Assert.Equal(1, Count(apply, "RoofGeneratedRafterSetService.TryReplaceForSupportedResize("));
        Assert.Equal(
            1,
            Count(
                apply,
                "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction("));
        Assert.Equal(1, Count(apply, "RoofAssemblyGroupSyncService.TrySyncForOwner("));
        Assert.Equal(1, Count(apply, "RoofDisplayService.EnsureAllDisplayBehindTimber("));
        Assert.Equal(1, Count(apply, "transaction.Commit();"));

        var ordinaryIndex = apply.IndexOf(
            "RoofGeneratedRafterSetService.TryReplaceForSupportedResize(",
            StringComparison.Ordinal);
        var structuralIndex = apply.IndexOf(
            "RoofAutomaticStructuralRafterMaterializationService.MaterializeInTransaction(",
            StringComparison.Ordinal);
        var groupIndex = apply.IndexOf(
            "RoofAssemblyGroupSyncService.TrySyncForOwner(",
            StringComparison.Ordinal);
        var drawOrderIndex = apply.IndexOf(
            "RoofDisplayService.EnsureAllDisplayBehindTimber(",
            StringComparison.Ordinal);
        var commitIndex = apply.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(ordinaryIndex >= 0);
        Assert.True(structuralIndex > ordinaryIndex);
        Assert.True(groupIndex > structuralIndex);
        Assert.True(drawOrderIndex > groupIndex);
        Assert.True(commitIndex > drawOrderIndex);
    }

    [Fact]
    public void GeneratedRefreshIsExistingSystemOnlyAndAnyFailureRollsBackApply()
    {
        var apply = Segment(
            EditWorkflow,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply(",
            "private static string GetSoftReplacementMessage(");

        Assert.Contains("existingOrdinaryGeneratedCount", apply);
        Assert.Contains("existingStructuralGeneratedCount", apply);
        Assert.Contains(
            "hadGeneratedRafterSystem = existingOrdinaryGeneratedCount > 0 ||",
            apply);
        Assert.Contains("geometryChanged &&", apply);
        Assert.Contains("existingOrdinaryGeneratedCount > 0 &&", apply);
        Assert.Contains(
            "outcome != RoofGeneratedRafterSetService.ReplacementOutcome.Replaced",
            apply);
        Assert.Contains(
            "hadGeneratedRafterSystem &&\n                restored.Geometry is HipRoofGeometry",
            apply.Replace("\r\n", "\n", StringComparison.Ordinal));

        var ordinaryFailure = apply.IndexOf(
            "outcome != RoofGeneratedRafterSetService.ReplacementOutcome.Replaced",
            StringComparison.Ordinal);
        var structuralFailure = apply.IndexOf("if (!structural.IsSuccess)", StringComparison.Ordinal);
        var commit = apply.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(ordinaryFailure >= 0 && ordinaryFailure < commit);
        Assert.True(structuralFailure >= 0 && structuralFailure < commit);
        Assert.Contains("return null;", apply[ordinaryFailure..commit]);
    }

    [Fact]
    public void StoredHipCurrentDisplayPathInspectsWithoutDuplicateRebuild()
    {
        var storedPath = Segment(
            CreateWorkflow,
            "if (storedDefinition.Exists)",
            "var footprint = validation.Footprint!;");

        Assert.Contains("InspectDisplay(", storedPath);
        Assert.Contains("RoofDisplayLifecycleKind.Current", storedPath);
        var currentIndex = storedPath.IndexOf(
            "lifecycle == RoofDisplayLifecycleKind.Current",
            StringComparison.Ordinal);
        var rebuildIndex = storedPath.IndexOf(
            "TryRebuildDisplay(",
            StringComparison.Ordinal);
        Assert.True(currentIndex >= 0);
        Assert.True(rebuildIndex > currentIndex);
        Assert.Contains("return;", storedPath[currentIndex..rebuildIndex]);
    }

    private static int Count(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(Repository, relative));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
