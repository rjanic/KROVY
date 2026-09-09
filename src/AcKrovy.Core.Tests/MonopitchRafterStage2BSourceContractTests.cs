using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2BSourceContractTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string Workflow = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs");
    private static readonly string Controller = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofRafterTransientPreviewController.cs");
    private static readonly string Transient = Read(
        "src/AcKrovy.AutoCAD/Infrastructure/RoofTransientPreviewSession.cs");
    private static readonly string Window = Read(
        "src/AcKrovy.AutoCAD/UI/RoofRafterWindow.xaml.cs");
    private static readonly string Validator = Read(
        "src/AcKrovy.Core/Services/Roofs/RoofRafterRequestValidator.cs");

    [Fact]
    public void CommandSelectionAcceptsAllThreeSupportedRoofGeometriesBeforeDialog()
    {
        var selection = Segment(
            Workflow,
            "private static bool TrySelectCurrentRoof(",
            "private static RoofRafterCreationResult TryCreateRafters(");

        Assert.Contains("restored.Geometry is not SimpleGableRoofGeometry and", selection);
        Assert.Contains("not MonopitchRoofGeometry", selection);
        Assert.Contains("IRoofGeometry Geometry", Workflow);
        Assert.DoesNotContain("MonopitchUnsupported", selection);
        Assert.True(
            Workflow.IndexOf("new RoofRafterWindow(", StringComparison.Ordinal) <
            Workflow.IndexOf("AcApp.ShowModalWindow(dialog)", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedDialogAndTransientRendererConsumeNeutralStage2ALayout()
    {
        Assert.Contains("private readonly IRoofGeometry _geometry", Window);
        Assert.Contains("internal RoofRafterLayout? PreviewLayout", Window);
        Assert.Contains("RoofRafterRequestValidator.Validate(", Window);
        Assert.Contains("RoofRafterLayoutSolver.Solve(", Validator);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver.Solve(", Validator);
        Assert.Contains("RoofRafterLayout layout", Transient);
        Assert.Contains("rafter.PlanStart,", Transient);
        Assert.Contains("rafter.PlanEnd,", Transient);
        Assert.Contains("segment.Start.X, segment.Start.Y, sourceElevation", Transient);
        Assert.Contains("segment.End.X, segment.End.Y, sourceElevation", Transient);
        Assert.DoesNotContain("GeometricExtents", Transient);
        Assert.DoesNotContain("Bounds", Transient);
    }

    [Fact]
    public void LiveRefreshDisposesOldTransientSetAndInvalidLayoutClearsIt()
    {
        Assert.Contains("PreviewLayoutChanged?.Invoke(PreviewLayout)", Window);
        Assert.Contains("dialog.PreviewLayoutChanged += previewChanged", Workflow);
        Assert.Contains("preview.Refresh(dialog.PreviewLayout)", Workflow);
        Assert.Contains("_current?.Dispose();", Controller);
        Assert.True(
            Controller.IndexOf("_current?.Dispose();", StringComparison.Ordinal) <
            Controller.IndexOf("_current = _show(layout);", StringComparison.Ordinal));
        Assert.Contains("preview.Refresh(null)", Workflow);
        Assert.Contains("using var preview", Workflow);
    }

    [Fact]
    public void CancelEscCloseAndInvalidPreviewStayBeforeEveryDatabaseWriteBoundary()
    {
        var run = Segment(
            Workflow,
            "public static void Run(Document document)",
            "private static bool TrySelectCurrentRoof(");
        var dialog = run.IndexOf("AcApp.ShowModalWindow(dialog)", StringComparison.Ordinal);
        var acceptedGuard = run.IndexOf("if (!accepted || dialog.Request is null)", StringComparison.Ordinal);
        var createCall = run.IndexOf("var result = TryCreateRafters(", StringComparison.Ordinal);

        Assert.True(dialog >= 0 && acceptedGuard > dialog);
        Assert.True(createCall > acceptedGuard);
        Assert.DoesNotContain("LockDocument", run[..createCall]);
        Assert.DoesNotContain("OpenMode.ForWrite", run[..createCall]);
        Assert.DoesNotContain("AppendEntity", run[..createCall]);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", run[..createCall]);
        Assert.DoesNotContain("Materialize(", run[..createCall]);
        Assert.DoesNotContain("RoofDefinitionStore.Write", run[..createCall]);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", run[..createCall]);
    }

    [Fact]
    public void AcceptedMonopitchUsesCurrentPersistedGeometryInsideSingleWriteScope()
    {
        var create = Segment(
            Workflow,
            "private static RoofRafterCreationResult TryCreateRafters(",
            "private static bool IsGeneratedSetStale(");
        var lockDocument = create.IndexOf("document.LockDocument()", StringComparison.Ordinal);
        var transaction = create.IndexOf(
            "TransactionManager.StartTransaction()",
            StringComparison.Ordinal);
        var restoredGuard = create.IndexOf(
            "restored.Geometry.Kind != expectedRoofKind",
            StringComparison.Ordinal);
        var validate = create.IndexOf(
            "RoofRafterRequestValidator.Validate(",
            StringComparison.Ordinal);
        var materialize = create.IndexOf(
            "RoofGeneratedRafterSetService.Materialize(",
            StringComparison.Ordinal);
        var commit = create.IndexOf("transaction.Commit();", StringComparison.Ordinal);

        Assert.True(lockDocument < transaction);
        Assert.True(transaction < restoredGuard);
        Assert.True(restoredGuard < validate && validate < materialize);
        Assert.True(materialize < commit);
        Assert.Equal(1, Count(create, "RoofGeneratedRafterSetService.Materialize("));
        Assert.Contains("restored.Geometry is not SimpleGableRoofGeometry and", create);
        Assert.Contains("not MonopitchRoofGeometry", create);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent(", create);
    }

    [Fact]
    public void MonopitchCreateIsEnabledAndUsesSharedMaterializationPath()
    {
        Assert.Contains("CreateButton.IsEnabled = validation.IsValid", Window);
        Assert.DoesNotContain("_geometry.Kind == RoofKind.Monopitch", Window);
        Assert.DoesNotContain("RoofRafterWindow_MonopitchPreviewOnly", Window);
        Assert.Contains("RoofRafterRequestValidator.Validate(", Workflow);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", Workflow);
        Assert.Contains("transaction.Commit();", Workflow);
    }

    [Fact]
    public void PreviewClassesContainNoPersistentDatabaseMutationApi()
    {
        var preview = Controller + Transient;
        Assert.DoesNotContain("LockDocument", preview);
        Assert.DoesNotContain("StartTransaction", preview);
        Assert.DoesNotContain("OpenMode.ForWrite", preview);
        Assert.DoesNotContain("AppendEntity", preview);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", preview);
        Assert.DoesNotContain("XData", preview);
        Assert.DoesNotContain("Group", preview);
        Assert.DoesNotContain("Materialize", preview);
        Assert.DoesNotContain("Annotation", preview);
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }

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
