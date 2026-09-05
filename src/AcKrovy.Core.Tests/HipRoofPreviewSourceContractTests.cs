using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofPreviewSourceContractTests
{
    private static readonly string Commands = Read("src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
    private static readonly string Workflow = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Preview = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofTransientPreviewSession.cs");
    private static readonly string ViewModel = Read("src", "AcKrovy.AutoCAD", "UI", "HipRoofPreviewViewModel.cs");
    private static readonly string Window = Read("src", "AcKrovy.AutoCAD", "UI", "HipRoofPreviewWindow.xaml");
    private static readonly string Ribbon = Read("src", "AcKrovy.AutoCAD", "Ribbon", "AcKrovyRibbon.cs");
    private static readonly string Catalog = Read("src", "AcKrovy.Localization", "CommandUiCatalog.cs");

    [Fact]
    public void RibbonUsesExistingHipEntryAndRoutesToSharedWorkflowPreset()
    {
        Assert.Equal("AK_ROOF_HIP", AcKrovyCommandNames.RoofHip);
        Assert.Equal(AcKrovyCommandNames.RoofHip, CommandUiCatalog.RoofHip.CommandName);
        Assert.Contains("Button(CommandUiCatalog.RoofHip, RibbonItemSize.Standard)", Ribbon);
        Assert.DoesNotContain("DisabledButton(CommandUiCatalog.RoofHip)", Ribbon);
        Assert.Contains("\"roof_hip\"", Catalog);
        Assert.Contains("[CommandMethod(AcKrovyCommandNames.RoofHip", Commands);
        Assert.Contains("RoofCommandWorkflow.Run(ActiveDocument(), RoofKind.Hip)", Commands);
    }

    [Fact]
    public void HipUsesValidatedGenericFootprintAndItsOwnNarrowPreviewDialog()
    {
        var route = Segment(Workflow, "private static void RunCreationDialog", "private static void RunHipPreviewDialog");
        Assert.Contains("if (initialKind == RoofKind.Hip)", route);
        Assert.Contains("RunHipPreviewDialog(", route);
        Assert.Contains("new GableRoofGeometryViewModel(footprint, initialKind)", route);
        Assert.Contains("RoofPolylineExtractor.Extract(polyline)", Workflow);
        Assert.Contains("RoofFootprintValidator.Validate(sourceInput)", Workflow);
        Assert.Contains("new HipRoofPreviewWindow(", Workflow);
        Assert.Contains("{localization:Loc RoofGeometryWindow_MonopitchSlope}", Window);
        Assert.DoesNotContain("ApplyButton", Window);
    }

    [Fact]
    public void HipSolverReceivesOnlyFootprintAndUniformSlopeWithoutRidgeDirection()
    {
        Assert.Contains("new RoofParameters(slope)", ViewModel);
        Assert.Contains("RoofKind.Hip", ViewModel);
        Assert.Contains("RoofGeometrySolver.Solve", ViewModel);
        Assert.DoesNotContain("RidgeDirection", ViewModel);
        Assert.DoesNotContain("SlopeDirection", ViewModel);
        var hipPath = Segment(Workflow, "private static void RunHipPreviewDialog", "private static void ClearCompletedWorkflowSelection");
        Assert.DoesNotContain("TryPromptOrientationDirection", hipPath);
        Assert.DoesNotContain("GetPoint", hipPath);
    }

    [Fact]
    public void PreviewMapsOnlyPhysicalHipTopologyEdgesWithoutHostRecomputation()
    {
        var hipMap = Segment(
            Preview,
            "internal static IReadOnlyList<RoofTopologyPreviewSegment> MapTopologySegments",
            "internal static IReadOnlyList<RoofPreviewSegment> MapRafterSegments");
        Assert.Contains("geometry.Topology.Edges", hipMap);
        Assert.Contains("RoofTopologyEdgeKind.Hip", hipMap);
        Assert.Contains("RoofTopologyEdgeKind.Ridge", hipMap);
        Assert.Contains("RoofTopologyEdgeKind.Valley", hipMap);
        Assert.DoesNotContain("RoofTopologyEdgeKind.Eave", hipMap);
        Assert.DoesNotContain("RoofTopologyEdgeKind.CoplanarSeam", hipMap);
        Assert.DoesNotContain("Math.", hipMap);
        Assert.DoesNotContain("Rectangle", hipMap);
    }

    [Fact]
    public void HipPreviewMethodHasStructuralPersistenceFirewall()
    {
        var hipPath = Segment(Workflow, "private static void RunHipPreviewDialog", "private static void ClearCompletedWorkflowSelection");
        Assert.DoesNotContain("RoofFootprintInput", hipPath);
        Assert.DoesNotContain("RoofDefinitionPersistence", hipPath);
        Assert.DoesNotContain("RoofDefinitionStore", hipPath);
        Assert.DoesNotContain("TryPersist", hipPath);
        Assert.DoesNotContain("OpenMode.ForWrite", hipPath);
        Assert.DoesNotContain("Transaction", hipPath);
        Assert.DoesNotContain("DocumentLock", hipPath);
        Assert.DoesNotContain("XData", hipPath);
        Assert.Contains("ShowPreview(document, geometry, sourceElevation)", hipPath);
        Assert.Contains("ClearCompletedWorkflowSelection(document.Editor)", hipPath);
        Assert.Contains("persistent-created=0", Preview);
    }

    [Fact]
    public void ExistingStoredRoofsCannotEnterHipPreviewLifecycle()
    {
        var selection = Segment(Workflow, "if (!validation.IsValid || validation.Footprint is null)", "if (storedDefinition.Exists)");
        Assert.Contains("requestedKind == RoofKind.Hip && storedDefinition.Exists", selection);
        Assert.Contains("Command_Roof_PersistConflict", selection);
        Assert.Contains("continue;", selection);
    }

    [Fact]
    public void ExistingCreateAndEditLabelsAndPersistenceRemainOnLegacyFlow()
    {
        var legacy = Segment(Workflow, "var footprint = validation.Footprint!;", "private static void RunHipPreviewDialog");
        Assert.Contains("new GableRoofGeometryWindow(", legacy);
        Assert.Contains("GableRoofGeometryDialogAction.PickRidgeDirection", legacy);
        Assert.Contains("GableRoofGeometryDialogAction.Apply", legacy);
        Assert.Contains("RoofDefinitionPersistence.Create", legacy);
        Assert.Contains("TryPersist(document, ownerId, data", legacy);
        Assert.DoesNotContain("HipRoofPreview", legacy);
    }

    [Fact]
    public void HipDiagnosticsAreDebugOnlyAndReportTopologyAndCleanupCounts()
    {
        Assert.Contains("#if DEBUG", Workflow);
        Assert.Contains("[AK_ROOF_HIP]", Workflow);
        Assert.Contains("RoofTopologyEdgeKind.CoplanarSeam", Workflow);
        Assert.Contains("preview-primitives=", Preview);
        Assert.Contains("[AK_ROOF_PREVIEW] cleanup", Preview);
        Assert.DoesNotContain("Editor.WriteMessage(\"[AK_ROOF_HIP]", Workflow);
    }

    [Fact]
    public void HipPersistenceContractsRemainDisabled()
    {
        var codec = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionDataCodec.cs");
        var persistence = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofDefinitionPersistence.cs");
        Assert.DoesNotContain("RoofKind.Hip =>", codec);
        Assert.Contains("_ => throw new ArgumentException(\"Unsupported roof geometry.\"", persistence);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
    }

    private static string Segment(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static string Read(params string[] path) => RoofUxSourceContractText.Read(path);
}
