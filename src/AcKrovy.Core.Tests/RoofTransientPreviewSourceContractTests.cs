using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofTransientPreviewSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Extractor = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofPolylineExtractor.cs");
    private static readonly string Preview = Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofTransientPreviewSession.cs");
    private static readonly string GeometryViewModel = Read(
        "src", "AcKrovy.AutoCAD", "UI", "GableRoofGeometryViewModel.cs");
    private static readonly string GeometryWindow = Read(
        "src", "AcKrovy.AutoCAD", "UI", "GableRoofGeometryWindow.xaml");

    [Fact]
    public void AkRoof_UsesStageOneSolverBeforeOpeningTransientPreview()
    {
        Assert.Contains("RoofGeometrySolver.Solve(new RoofDefinition(", GeometryViewModel);
        Assert.Contains("viewModel.TryGetGeometry", Workflow);
        Assert.Contains("RoofTransientPreviewSession.Show", Workflow);
        Assert.DoesNotContain("SimpleGableRoofGeometrySolver", Preview);
        Assert.DoesNotContain("Math.Tan", Preview);
        Assert.DoesNotContain("RunMm", Preview);
        Assert.DoesNotContain("RiseMm", Preview);
    }

    [Fact]
    public void PreviewMapping_AddsOnlySourceElevationToNeutralLocalZ()
    {
        Assert.Contains("MapSegments(geometry, sourceElevation)", Preview);
        Assert.Contains("SimpleGableRoofWireframe.Create(geometry, sourceElevation)", Preview);
        Assert.Contains("edge.Role == RoofDisplayEdgeRole.Ridge", Preview);
        Assert.DoesNotContain("face.BoundaryPoints", Preview);
        Assert.DoesNotContain("RoofPreviewSegmentKey", Preview);
        Assert.Contains("RoofPolylineExtractor.GetSourceElevation(polyline)", Workflow);
        Assert.Contains("polyline.GetPoint3dAt(0).Z", Extractor);
    }

    [Fact]
    public void Preview_UsesOnlyNonDatabaseTransientLines()
    {
        Assert.Contains("new Line(segment.Start, segment.End)", Preview);
        Assert.Contains("TransientManager.CurrentTransientManager", Preview);
        Assert.Contains("AddTransient", Preview);
        Assert.Contains("TransientDrawingMode.DirectShortTerm", Preview);
        Assert.Contains("EraseTransient", Preview);
        Assert.DoesNotContain("AppendEntity", Preview + Workflow);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", Preview + Workflow);
        Assert.DoesNotContain("BlockTableRecord", Preview + Workflow);
        Assert.DoesNotContain("ModelSpace", Preview + Workflow);
        Assert.DoesNotContain("PaperSpace", Preview + Workflow);
    }

    [Fact]
    public void PreviewLifecycle_IsScopedIdempotentAndDocumentAware()
    {
        Assert.Contains("IDisposable", Preview);
        Assert.Contains("using (RoofTransientPreviewSession.Show", Workflow);
        Assert.Contains("if (_disposed)", Preview);
        Assert.Contains("DocumentToBeDestroyed +=", Preview);
        Assert.Contains("DocumentToBeDestroyed -=", Preview);
        Assert.Contains("session.Dispose();", Preview);
        Assert.Contains("drawable.Dispose();", Preview);
        Assert.Contains("_drawables.Clear();", Preview);
    }

    [Fact]
    public void RoofPreviewPath_RemainsReadOnlyAndNonPersistent()
    {
        var previewPath = Segment(
            Workflow,
            "private static void ShowPreview",
            "private static bool ConfirmDisplayPersistence");
        var source = previewPath + Extractor + Preview;
        Assert.DoesNotContain("OpenMode.ForWrite", source);
        Assert.DoesNotContain("UpgradeOpen", source);
        Assert.DoesNotContain("transaction.Commit", source);
        Assert.DoesNotContain("polyline.Closed =", source);
        Assert.DoesNotContain("XData", source);
        Assert.DoesNotContain("Xrecord", source);
        Assert.DoesNotContain("DocumentLock", source);
        Assert.DoesNotContain("TimberElement", source);
    }

    [Fact]
    public void Parameters_AreExplicitLocalizedAndUseEditorWcsPointsDirectly()
    {
        Assert.DoesNotContain("editor.GetDouble", Workflow);
        Assert.Contains("editor.GetPoint", Workflow);
        Assert.Contains("RoofGeometryWindow_Alpha", GeometryWindow);
        Assert.Contains("RoofGeometryWindow_Beta", GeometryWindow);
        Assert.Contains("RoofGeometryWindow_DeltaHeight", GeometryWindow);
        Assert.Contains("Command_Roof_RidgeDirectionStartPrompt", Workflow);
        Assert.Contains("Command_Roof_RidgeDirectionEndPrompt", Workflow);
        Assert.Contains("RoofDirection2D.TryCreate", Workflow);
        Assert.Contains("directionStartResult.Value", Workflow);
        Assert.Contains("directionEndResult.Value", Workflow);
        Assert.DoesNotContain("UseDefaultValue = true", Workflow);
        Assert.DoesNotContain("CurrentUserCoordinateSystem", Workflow);
        Assert.DoesNotContain("TransformBy", Workflow);
    }

    [Fact]
    public void CorrectableGeometryUsesSharedWpfWhileTechnicalNonFiniteStaysCli()
    {
        Assert.Contains("new GableRoofGeometryWindow(", Workflow);
        Assert.Contains("RoofGeometrySolver.Solve", GeometryViewModel);
        Assert.Contains("RoofGeometryWindow_ValidationNumber", GeometryViewModel);
        Assert.Contains("RoofGeometryWindow_ValidationCombination", GeometryViewModel);
        Assert.Contains("double.IsFinite(value)", GeometryViewModel);
        Assert.Contains("GetStoredDefinitionMessage", Workflow);
        Assert.Contains("notification = OpenLoopNotification", Workflow);
        Assert.Contains("TransientNotificationService.Show", Workflow);
        Assert.Equal(1, CountOccurrences(Workflow, "TransientNotificationService.Show"));
    }

    [Fact]
    public void CoreRoofSources_StillContainNoAutodeskApiReference()
    {
        var roofSources = Directory.GetFiles(
            Path.Combine(Repository, "src", "AcKrovy.Core"),
            "*Roof*.cs",
            SearchOption.AllDirectories);

        Assert.NotEmpty(roofSources);
        Assert.All(roofSources, file =>
            Assert.DoesNotContain("Autodesk", File.ReadAllText(file)));
    }

    private static int CountOccurrences(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;

    private static string Segment(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static string Read(params string[] path) =>
        RoofUxSourceContractText.Read(path);

    private static string RepositoryRoot()
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
