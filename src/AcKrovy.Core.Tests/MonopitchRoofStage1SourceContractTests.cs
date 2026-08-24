using System.Xml.Linq;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRoofStage1SourceContractTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void CreationCommandAndRibbon_DispatchToSharedRoofWorkflow()
    {
        var commands = Read("src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs");
        var catalog = Read("src/AcKrovy.Localization/CommandUiCatalog.cs");
        var ribbon = Read("src/AcKrovy.AutoCAD/Ribbon/AcKrovyRibbon.cs");

        Assert.Contains("AK_ROOF_MONOPITCH", catalog);
        Assert.Contains("RoofCommandWorkflow.Run(ActiveDocument(), RoofKind.Monopitch)", commands);
        Assert.Contains("Button(CommandUiCatalog.RoofMonoPitch", ribbon);
        Assert.DoesNotContain("DisabledButton(CommandUiCatalog.RoofMonoPitch)", ribbon);
    }

    [Fact]
    public void CreationAndEdit_PickHighThenLowAndConvertToCanonicalDirection()
    {
        var creation = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
        var edit = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
        var window = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryWindow.xaml");
        var creationPrompt = Member(creation, "private static bool TryPromptOrientationDirection(");
        var editPrompt = Member(edit, "private static bool TryPromptOrientationDirection(");

        Assert.True(
            creationPrompt.IndexOf("Command_Roof_HighSidePointPrompt", StringComparison.Ordinal) <
            creationPrompt.IndexOf("Command_Roof_LowSidePointPrompt", StringComparison.Ordinal));
        Assert.True(
            editPrompt.IndexOf("Command_Roof_HighSidePointPrompt", StringComparison.Ordinal) <
            editPrompt.IndexOf("Command_Roof_LowSidePointPrompt", StringComparison.Ordinal));
        Assert.Contains(
            "MonopitchRoofDirectionPresentationRules.ToCanonicalLowToHigh(",
            creationPrompt);
        Assert.Contains(
            "MonopitchRoofDirectionPresentationRules.ToCanonicalLowToHigh(",
            editPrompt);
        Assert.Contains("new GableRoofGeometryWindow(", creation);
        Assert.Contains("new GableRoofGeometryWindow(", edit);
        Assert.Contains("IRoofGeometry restoredGeometry", edit);
        Assert.Contains("MonopitchRoofSectionControl", window);
        Assert.Contains("MirrorMonopitchCheckBox", window);

        Assert.Contains("\"\\n\" + directionStartPrompt", creationPrompt);
        Assert.Contains("\"\\n\" + directionEndPrompt", creationPrompt);
        Assert.Contains("\"\\n\" + directionStartPrompt", editPrompt);
        Assert.Contains("\"\\n\" + directionEndPrompt", editPrompt);
    }

    [Fact]
    public void MonopitchCreation_DefersDirectionToOneExplicitEditorAction()
    {
        var creation = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
        var creationDialog = Between(
            creation,
            "private static void RunCreationDialog(",
            "private static void ClearCompletedWorkflowSelection(");
        var editorConstruction = creationDialog.IndexOf(
            "new GableRoofGeometryWindow(",
            StringComparison.Ordinal);
        var dialogLoop = creationDialog.IndexOf("while (!dialog.IsClosed)", StringComparison.Ordinal);
        var repickAction = creationDialog.IndexOf(
            "case GableRoofGeometryDialogAction.PickRidgeDirection:",
            StringComparison.Ordinal);
        var directionPicker = creationDialog.IndexOf(
            "TryPromptOrientationDirection(",
            StringComparison.Ordinal);

        Assert.True(editorConstruction >= 0);
        Assert.True(dialogLoop >= 0);
        Assert.True(repickAction >= 0);
        Assert.True(directionPicker >= 0);
        Assert.True(editorConstruction < dialogLoop);
        Assert.True(dialogLoop < repickAction);
        Assert.True(repickAction < directionPicker);
        Assert.Equal(1, Count(creationDialog, "TryPromptOrientationDirection("));
        Assert.DoesNotContain("if (initialKind == RoofKind.Monopitch)", creationDialog);
        Assert.Contains("viewModel.SetRidgeDirection(direction);", creationDialog);
    }

    [Fact]
    public void DirectionPick_CancelReturnsToTheSharedEditorWithoutCommittingOrRetrying()
    {
        var creation = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
        var edit = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
        var creationPrompt = Between(
            creation,
            "private static bool TryPromptOrientationDirection(",
            "private static IntPtr TryGetAutoCadMainWindowHandle()");
        var editPrompt = Between(
            edit,
            "private static bool TryPromptOrientationDirection(",
            "private static IntPtr TryGetAutoCadMainWindowHandle()");

        Assert.Contains("directionStartResult.Status != PromptStatus.OK", creationPrompt);
        Assert.Contains("directionEndResult.Status != PromptStatus.OK", creationPrompt);
        Assert.Contains("directionStartResult.Status != PromptStatus.OK", editPrompt);
        Assert.Contains("directionEndResult.Status != PromptStatus.OK", editPrompt);
        Assert.Contains("if (TryPromptOrientationDirection(", creation);
        Assert.Contains("if (TryPromptOrientationDirection(", edit);
        Assert.Contains("viewModel.SetRidgeDirection(direction);", creation);
        Assert.Contains("viewModel.SetRidgeDirection(direction);", edit);
    }

    [Fact]
    public void CommandLinePrompts_AreConciseAndNeverContainLiteralBackslashN()
    {
        var expectedSlovak = new Dictionary<string, string>
        {
            ["Command_RoofEdit_SelectPrompt"] = "Vyber strechu: ",
            ["Command_Roof_HighSidePointPrompt"] = "Urči VYSOKÝ okap: ",
            ["Command_Roof_LowSidePointPrompt"] = "Urči NÍZKY okap: ",
        };

        foreach (var path in Directory.GetFiles(
                     Path.Combine(Root, "src/AcKrovy.Localization/Resources"),
                     "UiStrings*.resx"))
        {
            var document = XDocument.Load(path);
            var values = document.Root!
                .Elements("data")
                .ToDictionary(
                    item => (string)item.Attribute("name")!,
                    item => (string)item.Element("value")!);

            foreach (var key in expectedSlovak.Keys)
            {
                Assert.True(values.ContainsKey(key), $"Missing {key} in {path}");
                Assert.DoesNotContain("\\n", values[key], StringComparison.Ordinal);
                Assert.DoesNotContain("\r", values[key], StringComparison.Ordinal);
                Assert.DoesNotContain("\n", values[key], StringComparison.Ordinal);
            }
        }

        var slovak = XDocument.Load(Path.Combine(
            Root,
            "src/AcKrovy.Localization/Resources/UiStrings.resx"));
        var slovakValues = slovak.Root!
            .Elements("data")
            .ToDictionary(
                item => (string)item.Attribute("name")!,
                item => (string)item.Element("value")!);
        foreach (var pair in expectedSlovak)
        {
            Assert.Equal(pair.Value, slovakValues[pair.Key]);
        }
    }

    [Fact]
    public void PreviewAndPermanentDisplay_DispatchAtGeometryBoundary()
    {
        var preview = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofTransientPreviewSession.cs");
        var display = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofDisplayService.cs");
        var dispatch = Read("src/AcKrovy.Core/Services/Roofs/RoofWireframe.cs");

        Assert.Contains("IRoofGeometry geometry", preview);
        Assert.Contains("RoofDisplayEdgeRole.MonopitchDirection", preview);
        Assert.Contains("RoofWireframe.Create", display);
        Assert.Contains("MonopitchRoofWireframe.Create", dispatch);
    }

    [Fact]
    public void RafterWorkflow_RoutesMonopitchThroughSharedValidatedMaterialization()
    {
        var source = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs");
        var selectionCall = source.IndexOf("TrySelectCurrentRoof(document", StringComparison.Ordinal);
        var dialog = source.IndexOf("new RoofRafterWindow(", StringComparison.Ordinal);
        var createMethod = source.IndexOf("private static RoofRafterCreationResult TryCreateRafters(", StringComparison.Ordinal);
        var lockDocument = source.IndexOf("document.LockDocument()", createMethod, StringComparison.Ordinal);
        var validation = source.IndexOf(
            "RoofRafterRequestValidator.Validate(",
            createMethod,
            StringComparison.Ordinal);
        var materialize = source.IndexOf("RoofGeneratedRafterSetService.Materialize(", StringComparison.Ordinal);

        Assert.True(selectionCall >= 0);
        Assert.True(dialog >= 0);
        Assert.True(createMethod >= 0);
        Assert.True(lockDocument >= 0);
        Assert.True(validation >= 0);
        Assert.True(materialize >= 0);
        Assert.True(selectionCall < dialog);
        Assert.True(dialog < lockDocument);
        Assert.True(lockDocument < validation);
        Assert.True(validation < materialize);
        Assert.Contains("restored.Geometry is not SimpleGableRoofGeometry and", source);
        Assert.Contains("not MonopitchRoofGeometry", source);
        Assert.Contains("RoofRafterMaterializationRules.IsConsistent(", source);
    }

    [Fact]
    public void MonopitchGeometryAndPersistence_AreCadNeutralAndPolylineTopologyOnly()
    {
        var files = new[]
        {
            "src/AcKrovy.Core/Models/Roofs/MonopitchRoofGeometry.cs",
            "src/AcKrovy.Core/Services/Roofs/MonopitchRoofGeometrySolver.cs",
            "src/AcKrovy.Core/Services/Roofs/MonopitchRoofMath.cs",
            "src/AcKrovy.Core/Models/Roofs/RoofRafterPlane.cs",
            "src/AcKrovy.Core/Models/Roofs/RoofRafterGeometry.cs",
            "src/AcKrovy.Core/Models/Roofs/RoofRafterLayout.cs",
            "src/AcKrovy.Core/Services/Roofs/RoofRafterLayoutSolver.cs",
            "src/AcKrovy.Core/Services/Roofs/RoofDefinitionPersistence.cs",
        };
        var source = string.Join("\n", files.Select(Read));

        Assert.DoesNotContain("Autodesk", source);
        Assert.DoesNotContain("ObjectId", source);
        Assert.DoesNotContain("GeometricExtents", source);
        Assert.DoesNotContain("GetBoundingBox", source);
        Assert.Contains("TryReadSourceTopology", source);
    }

    [Fact]
    public void ProductAndLifecycleSchemas_RemainUnchanged()
    {
        var props = Read("Directory.Build.props");
        var schema = Read("src/AcKrovy.Core/Models/Roofs/RoofDefinitionDataSchema.cs");
        var attached = Read("src/AcKrovy.Core/Models/Roofs/RoofAttachedManualTimberDataSchema.cs");

        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", props);
        Assert.Contains("<Version>$(AcKrovyVersion)</Version>", props);
        Assert.Contains("CurrentVersion = 5", schema);
        Assert.Contains("CurrentVersion = 3", attached);
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string Member(string source, string start)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing member: {start}");
        return source.Substring(startIndex);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start: {start}");
        Assert.True(endIndex > startIndex, $"Missing end: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

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
