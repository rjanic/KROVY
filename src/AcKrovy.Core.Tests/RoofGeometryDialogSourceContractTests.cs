using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeometryDialogSourceContractTests
{
    private static readonly string Repository = RepositoryRoot();
    private static readonly string Workflow = Read("src/AcKrovy.AutoCAD/Infrastructure/RoofCommandWorkflow.cs");
    private static readonly string Commands = Read("src/AcKrovy.AutoCAD/Commands/AcKrovyCommands.cs");
    private static readonly string ViewModel = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryViewModel.cs");
    private static readonly string Layout = Read("src/AcKrovy.AutoCAD/UI/GableRoofSectionLayout.cs");
    private static readonly string Control = Read("src/AcKrovy.AutoCAD/UI/GableRoofSectionControl.cs");
    private static readonly string Window = Read("src/AcKrovy.AutoCAD/UI/GableRoofGeometryWindow.xaml");
    private static readonly string Codec = Read("src/AcKrovy.Core/Services/Roofs/RoofDefinitionDataCodec.cs");

    [Fact]
    public void ProductionRoofCreation_OpensSharedDialogAndHasNoDirectSlopePrompt()
    {
        Assert.Contains("new GableRoofGeometryWindow(", Workflow);
        Assert.Contains("AcApp.ShowModalWindow(dialog)", Workflow);
        Assert.Contains("RunCreationDialog(", Workflow);
        Assert.DoesNotContain("GetDouble(", Workflow);
        Assert.DoesNotContain("Command_Roof_SlopePrompt", Workflow);
        Assert.DoesNotContain("Command_RoofAsym_Face0SlopePrompt", Workflow);
    }

    [Fact]
    public void BothCommandsRouteIntoOneWorkflowAndOneWindowArchitecture()
    {
        Assert.Contains("RoofCommandWorkflow.Run(ActiveDocument())", Commands);
        Assert.Contains("RoofCommandWorkflow.Run(ActiveDocument(), RoofKind.AsymmetricGable)", Commands);
        Assert.Equal(1, Count(Workflow, "new GableRoofGeometryWindow("));
    }

    [Fact]
    public void DialogActionsKeepPickingAndPreviewSeparateFromApplyPersistence()
    {
        var pick = Segment(
            "case GableRoofGeometryDialogAction.PickRidgeDirection:",
            "case GableRoofGeometryDialogAction.Preview:");
        var preview = Segment(
            "case GableRoofGeometryDialogAction.Preview:",
            "case GableRoofGeometryDialogAction.Apply:");
        var apply = Segment(
            "case GableRoofGeometryDialogAction.Apply:",
            "default:");

        Assert.DoesNotContain("TryPersist", pick + preview);
        Assert.Contains("TryPersist(document, ownerId, data", apply);
        Assert.Contains("ShowPreview(document, previewGeometry, sourceElevation)", Workflow);
        Assert.Contains("while (!dialog.IsClosed)", Workflow);
        Assert.Contains("default:\n                        return;", Workflow.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SchemaBoundariesRemainExplicit()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
    }

    [Fact]
    public void SchematicUsesOneUniformScaleAndAnglesRemainMeasuredFromHorizontal()
    {
        Assert.Contains("var uniformScale = Math.Min(", Layout);
        Assert.Contains("uniformScale,\n            uniformScale,", Layout.Replace("\r\n", "\n"));
        Assert.Contains("enteredRunA * Math.Tan(alpha", ViewModel);
        Assert.Contains("runB * Math.Tan(beta", ViewModel);
    }

    [Fact]
    public void AsymmetricInputModeRemainsUiOnlyAndSchemaFiveIsUnchanged()
    {
        Assert.Contains("enum AsymmetricGableInputMode", ViewModel);
        Assert.DoesNotContain("AsymmetricGableInputMode", Codec);
        Assert.DoesNotContain("AsymmetricGableInputMode", Workflow);
        Assert.Contains("CurrentVersion => EncodeV5", Codec);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Fact]
    public void MirrorRemainsUiOnlyAndMapsIntoSchemaFivePhysicalParameters()
    {
        Assert.Contains("IsAsymmetryMirrored", ViewModel);
        Assert.Contains("IsAsymmetryMirrored ? beta : alpha", ViewModel);
        Assert.Contains("IsAsymmetryMirrored ? alpha : beta", ViewModel);
        Assert.Contains("? -uiDeltaHeight", ViewModel);
        Assert.DoesNotContain("IsAsymmetryMirrored", Codec);
        Assert.DoesNotContain("IsAsymmetryMirrored", Workflow);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Fact]
    public void RefinedWindowAndSchematicKeepTheRequestedVisualContracts()
    {
        Assert.Contains("x:Key=\"RoofModeCardStyle\"", Window);
        Assert.Contains("SymmetricRoofTypeIcon", Window);
        Assert.Contains("AsymmetricRoofTypeIcon", Window);
        Assert.Contains("PickRidgeDirectionIcon", Window);
        Assert.Contains("Foreground=\"{DynamicResource SettingsTextPrimaryBrush}\"", Window);
        Assert.Contains("MirrorAsymmetryCheckBox", Window);
        Assert.DoesNotContain("<ScrollViewer", Window);
        Assert.Contains("state.IsMirrored ? 0d : state.SpanMm", Layout);
        Assert.Contains("state.IsMirrored ? state.RunAMm : state.RunBMm", Layout);
        Assert.Contains("new Pen(leftBrush, 12d)", Control);
        Assert.Contains("new Pen(rightBrush, 12d)", Control);
        Assert.Contains("state.RidgeLabel", Control);
        Assert.Contains("CreateAngleAnnotation", Control);
        Assert.Contains("DrawArcArrow", Control);
        Assert.Contains("outwardY", Control);
        Assert.Contains("y - 22d", Control);
        Assert.Contains("DrawDimensionArrow", Control);
        Assert.Contains("DrawVerticalCenteredText", Control);
        Assert.Contains("ToString(\"0\", culture)", Control);
        Assert.Contains("ToString(\"+0;-0;0\", culture)", Control);
    }

    private static int Count(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

    private static string Segment(string start, string end)
    {
        var startIndex = Workflow.IndexOf(start, StringComparison.Ordinal);
        var endIndex = Workflow.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return Workflow[startIndex..endIndex];
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
