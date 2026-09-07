using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterSpacingSourceContractTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Store = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure",
        "AutoCadRoofRafterSpacingStore.cs");

    [Fact]
    public void StoreUsesSeparateSchemaOneDrawingRecordWithoutChangingAnnotationPayload()
    {
        var annotationStore = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadDrawingAnnotationScaleStore.cs");

        Assert.Contains("DrawingSettingsRecordName = \"ROOF_RAFTER_SETTINGS\"", Store);
        Assert.Contains("private const int SchemaVersion = 1", Store);
        Assert.Contains("DxfCode.Real", Store);
        Assert.Contains("settings.DefaultAutomaticSpacingMm", Store);
        Assert.Contains("settings.MinimumAutomaticSpacingMm", Store);
        Assert.Contains("RoofRafterSpacingRules.IsValidSettings", Store);
        Assert.Contains("values.Count != 3", Store);
        Assert.DoesNotContain("RafterSpacing", annotationStore);
        Assert.Contains("values.Count != 2", annotationStore);
    }

    [Fact]
    public void MissingValueReadIsStrictlyReadOnlyAndResolvedByCore()
    {
        var read = Segment(Store, "public bool TryRead", "public void Write");
        var effective = Segment(
            Store,
            "public static RoofRafterSettings ReadEffective",
            "private DBDictionary");

        Assert.Contains("OpenMode.ForRead", read);
        Assert.DoesNotContain("OpenMode.ForWrite", read);
        Assert.DoesNotContain("UpgradeOpen", read);
        Assert.DoesNotContain("SetAt(", read);
        Assert.DoesNotContain("Commit(", read + effective);
        Assert.Contains("RoofRafterSpacingRules.Resolve", effective);
    }

    [Fact]
    public void LayoutAlgorithmReceivesSpacingAndContainsNoPolicyConstants()
    {
        var service = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofFaceRafterLayoutService.cs");

        Assert.Contains("double spacingMm", service);
        Assert.DoesNotContain("500", service);
        Assert.DoesNotContain("900", service);
        Assert.DoesNotContain("Autodesk", service);
        Assert.DoesNotContain("ObjectId", service);
        Assert.DoesNotContain("Database", service);
    }

    [Fact]
    public void MinimumPolicyIsScopedToAutomaticWorkflowNotManualTimber()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var validator = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterRequestValidator.cs");
        var commands = Read(
            "src", "AcKrovy.AutoCAD", "Commands",
            "AcKrovyCommands.cs");

        Assert.Contains("minimumAutomaticSpacingMm", workflow);
        Assert.Contains("IsValidAutomaticWorkingSpacing", validator);
        var manualAssignment = Segment(
            commands,
            "private static void AssignSelectedElements",
            "[CommandMethod(AcKrovyCommandNames.Renumber");
        Assert.DoesNotContain("RoofRafterSpacingRules", manualAssignment);
        Assert.DoesNotContain("500", manualAssignment);
    }

    [Fact]
    public void FaceSlicingEmitsSeparateIntervalsWithoutHostShapeHeuristics()
    {
        var service = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofFaceRafterLayoutService.cs");
        var model = Read(
            "src", "AcKrovy.Core", "Models", "Roofs",
            "RoofFaceRafterLayout.cs");
        var hostWindow = Read(
            "src", "AcKrovy.AutoCAD", "UI",
            "RoofRafterWindow.xaml.cs");
        var preview = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterTransientPreviewController.cs");

        Assert.Contains("TryCreateFaceStations", service);
        Assert.Contains("ResolveRidgeCoordinatedPhases", service);
        Assert.Contains("CollectCompatibleRidgeEdges", service);
        Assert.Contains("AreConnectedRidgeComponentEdges", service);
        Assert.Contains("TryCreateComponentPhase", service);
        Assert.Contains("FindFaceIntervals", service);
        Assert.Contains("index + 1 < hits.Count", service);
        Assert.Contains("IsStrictlyInside(midpoint, polygon)", service);
        Assert.Contains("StartBoundaryRole", model);
        Assert.Contains("EndBoundaryRole", model);
        Assert.Contains("StationIntervalIndex", model);
        Assert.Contains("string.CompareOrdinal(first, second)", service);
        Assert.Contains("SourceFaceIndex", Segment(service, "private static string SegmentKey", "private static string PointKey"));
        Assert.DoesNotContain("LShape", service + hostWindow + preview);
        Assert.DoesNotContain("UShape", service + hostWindow + preview);
        Assert.DoesNotContain("TShape", service + hostWindow + preview);
        Assert.DoesNotContain("vertexCount", service + hostWindow + preview);
        Assert.DoesNotContain("BoundingBox", service + hostWindow + preview);
        Assert.DoesNotContain("globalRidge", service + preview);
        Assert.DoesNotContain("RidgeDirection", preview);
        Assert.DoesNotContain("phase", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("offset", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RidgePhaseCoordinationStaysInCoreNotHostPreview()
    {
        var service = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofFaceRafterLayoutService.cs");
        var preview = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterTransientPreviewController.cs");
        var window = Read(
            "src", "AcKrovy.AutoCAD", "UI",
            "RoofRafterWindow.xaml.cs");

        Assert.Contains("AbsolutePhaseT", service);
        Assert.Contains("CanonicalAxis", service);
        Assert.Contains("AreCompatibleOpposingRafterFamilies", service);
        Assert.Contains("CompatibleRidgeEdge", service);
        Assert.Contains("RoofFaceRafterLayoutService.Create", window);
        Assert.DoesNotContain("ResolveRidgeCoordinatedPhases", preview + window);
        Assert.DoesNotContain("CreateStationDistances", preview);
        Assert.DoesNotContain("AreEquivalentPhases", service);
        Assert.DoesNotContain("Rectangle", preview);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string Segment(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0, "Missing start: " + start);
        Assert.True(last > first, "Missing end: " + end);
        return source.Substring(first, last - first);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
