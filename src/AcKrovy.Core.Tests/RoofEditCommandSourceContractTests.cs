using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract guards for AK_ROOF_EDIT: the dedicated command routes to the
/// edit workflow, the shared GableRoofGeometryWindow/ViewModel are reused with a
/// seed-from-existing-geometry path, the read-only phase (open/preview/cancel)
/// never writes, and Apply replays the canonical rebuild pipeline (definition
/// rebase, display rebuild, generated-set replacement, anchored AttachedManual
/// replay, indicator/selectability/group sync) without the create conflict path.
/// </summary>
public sealed class RoofEditCommandSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Commands = Read("src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs");
    private static readonly string Catalog = Read("src/AcKrovy.Localization/CommandUiCatalog.cs");
    private static readonly string EditWorkflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
    private static readonly string CreateWorkflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
    private static readonly string ViewModel = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryViewModel.cs");

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
            "var outcome = RoofGeneratedRafterSetService.TryReplaceForSupportedResize(",
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
