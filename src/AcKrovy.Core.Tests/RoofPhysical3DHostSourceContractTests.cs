using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Source contracts for physical-3D hip roof host integration.</summary>
public sealed class RoofPhysical3DHostSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Materialization = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPhysical3DMaterializationService.cs");
    private static readonly string ElevationStore = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPhysicalElevationStore.cs");
    private static readonly string CreateWorkflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string EditWorkflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofEditCommandWorkflow.cs");
    private static readonly string Resize = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Copy = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofWholeRoofCopyRebindService.cs");
    private static readonly string LiveGeometry = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string OwnerSelection = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOwnerSelectionResolver.cs");
    private static readonly string HipXaml = Read(
        "src", "AcKrovy.AutoCAD", "UI", "HipRoofPreviewWindow.xaml");

    [Fact]
    public void Materialization_UsesResolvedEaveOnly_NeverPolylineElevation()
    {
        Assert.Contains("polyline.Elevation is never added", Materialization);
        Assert.DoesNotContain("GetSourceElevation", Materialization);
        Assert.Contains("EraseOwned", Materialization);
        Assert.Contains("RoofPhysical3DSuspensionRules", Materialization);
        Assert.Contains("PreservePreferenceWhileSuspended", Materialization);
        Assert.Contains("RoofPhysical3DPlanDisplayRules.FaceEdgeVisibility", Materialization);
        Assert.Contains("RoofPhysical3DGeneratedRole.EaveEdge", Materialization);
        Assert.DoesNotContain(
            "WithPhysical3DEnabled(elevation, false)",
            Materialization);
        Assert.Contains("new Face(", Materialization);
        Assert.Contains("foreach (var eave in model.Eaves)", Materialization);
    }

    [Fact]
    public void ElevationStore_IsIndependentOfDefinitionSchema5()
    {
        Assert.Contains("DECORAIR_ACADKROVY_ROOF_PHYSICAL_ELEVATION", ElevationStore);
        Assert.Contains("RoofPhysicalElevationRules.Validate", ElevationStore);
        Assert.Contains("Physical3DEnabled", ElevationStore);
        Assert.Contains("DisplayVisibility", ElevationStore);
        Assert.Contains("ApplyOwnedVisibility", Materialization);
        Assert.DoesNotContain("RoofDefinitionDataSchema", ElevationStore);
        Assert.DoesNotContain("EncodeV5", ElevationStore);
    }

    [Fact]
    public void PermanentDisplayPaths_UseFlattenAwareOwnedEdges()
    {
        Assert.Contains("CreateOwnedHipOrLegacy", CreateWorkflow);
        Assert.Contains("CreateOwnedDisplayEdges", CreateWorkflow);
        Assert.Contains("CreateOwnedHipOrLegacy", EditWorkflow);
        Assert.Contains("CreateOwnedHipOrLegacy", Resize);
        Assert.Contains("CreateOwnedDisplayEdges", Resize);
        Assert.Contains("CreateOwnedHipOrLegacy", Copy);
        Assert.Contains("CreateOwnedDisplayEdges", Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs"));
        Assert.Contains("FlattenedDrawingPlane", Read(
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofWireframe.cs"));
        Assert.Contains("FilterOwnedPhysical3DPlanEdges", Read(
            "src", "AcKrovy.Core", "Services", "Roofs", "RoofWireframe.cs"));
        Assert.Contains("ApplyOwnedVisibility", Materialization);
        Assert.Contains("entity.Visible", Materialization);
    }

    [Fact]
    public void CreateEditLifecycle_ReconcilesPhysical3D()
    {
        Assert.Contains("RoofPhysical3DLifecycleService.ReconcileOwnerInTransaction", CreateWorkflow);
        Assert.Contains("TryGetElevationState", CreateWorkflow);
        Assert.Contains("RoofPhysical3DLifecycleService.ReconcileOwnerInTransaction", EditWorkflow);
        Assert.Contains("seedElevation", EditWorkflow);
    }

    [Fact]
    public void NativeLifecycle_HooksStretchCopyAndErase_SkipUndoRedo()
    {
        Assert.Contains("RoofPhysical3DLifecycleService.ReconcileOwnerInTransaction", Resize);
        Assert.Contains("RoofPhysical3DLifecycleService.ReconcileOwnerInTransaction", Copy);
        Assert.Contains("RoofPhysical3DLifecycleService.CleanupStillErasedSourceInTransaction", LiveGeometry);
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", LiveGeometry);
        Assert.Contains("Physical3DSuspendedNotificationTitle", Resize);
        Assert.Contains("SuspendedDueToIneligibility", Resize);
    }

    [Fact]
    public void OwnerSelection_ResolvesPhysical3DChildren()
    {
        Assert.Contains("RoofPhysical3DGeneratedStore.Read", OwnerSelection);
    }

    [Fact]
    public void ExplicitSourceSelection_IsReadOnlyAndSelectsOnlyResolvedOwner()
    {
        var selection = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofSourceSelectionWorkflow.cs");
        Assert.Contains("RoofOwnerSelectionResolver.Resolve", selection);
        Assert.Contains("OpenMode.ForRead", selection);
        Assert.Contains("SetImpliedSelection(new[] { ownerId })", selection);
        Assert.DoesNotContain("OpenMode.ForWrite", selection);
        Assert.DoesNotContain("transaction.Commit", selection);
        Assert.DoesNotContain("LayerTable", selection);
        Assert.DoesNotContain("Selectable =", selection);
        Assert.Contains("AcKrovyCommandNames.RoofSelectSource", Read(
            "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs"));
    }

    [Fact]
    public void HipDialog_ExposesPhysical3DCheckboxAndElevationModes()
    {
        Assert.Contains("Physical3DEnabled", HipXaml);
        Assert.Contains("IsEaveElevationMode", HipXaml);
        Assert.Contains("SupportsPhysicalElevation", HipXaml);
        Assert.Contains("RoofHip_ElevationSection", HipXaml);
        Assert.Contains("ShowDisplayVisibilityOptions", HipXaml);
        Assert.Contains("IsDisplayBoth", HipXaml);
        Assert.Contains("IsDisplayPlan2D", HipXaml);
        Assert.Contains("IsDisplayModel3D", HipXaml);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Repository, .. path]))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

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
