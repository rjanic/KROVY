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
    public void CreationAndEdit_UseLowHighPromptsAndCommonEditorShell()
    {
        var creation = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
        var edit = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofEditCommandWorkflow.cs");
        var window = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryWindow.xaml");

        Assert.Contains("Command_Roof_LowSidePointPrompt", creation);
        Assert.Contains("Command_Roof_HighSidePointPrompt", creation);
        Assert.Contains("new GableRoofGeometryWindow(", creation);
        Assert.Contains("new GableRoofGeometryWindow(", edit);
        Assert.Contains("IRoofGeometry restoredGeometry", edit);
        Assert.Contains("MonopitchRoofSectionControl", window);
        Assert.Contains("MirrorMonopitchCheckBox", window);
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
    public void RafterWorkflow_RejectsMonopitchBeforeDialogOrMaterialization()
    {
        var source = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofRafterCommandWorkflow.cs");
        var guard = source.IndexOf("restored.Geometry.Kind == RoofKind.Monopitch", StringComparison.Ordinal);
        var dialog = source.IndexOf("new RoofRafterWindow(", StringComparison.Ordinal);

        Assert.True(guard >= 0);
        Assert.True(dialog >= 0);
        Assert.Contains("Command_RoofRafters_MonopitchUnsupported", source);
        Assert.Contains("RoofRafterCreationResult.Failure(\n                    \"Command_RoofRafters_MonopitchUnsupported\")", source);
    }

    [Fact]
    public void MonopitchGeometryAndPersistence_AreCadNeutralAndPolylineTopologyOnly()
    {
        var files = new[]
        {
            "src/AcKrovy.Core/Models/Roofs/MonopitchRoofGeometry.cs",
            "src/AcKrovy.Core/Services/Roofs/MonopitchRoofGeometrySolver.cs",
            "src/AcKrovy.Core/Services/Roofs/MonopitchRoofMath.cs",
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
