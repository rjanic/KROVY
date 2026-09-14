using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AutomaticPurlinDialogAutoCadSourceContractTests
{
    private static readonly string Command = Read(
        "Commands",
        "AutoCadAutomaticPurlinDialogCommands.cs");
    private static readonly string Window = Read(
        "UI",
        "AutomaticPurlinDialogWindow.xaml");
    private static readonly string WindowCodeBehind = Read(
        "UI",
        "AutomaticPurlinDialogWindow.xaml.cs");
    private static readonly string ViewModel = Read(
        "UI",
        "AutomaticPurlinDialogViewModel.cs");
    private static readonly string Transient = Read(
        "Infrastructure",
        "RoofTransientPreviewSession.cs");

    [Fact]
    public void DebugDialog_IsReleaseIsolatedAndReadsOwnerForReadOnly()
    {
        Assert.StartsWith("#if DEBUG", Command.TrimStart());
        Assert.Contains("AK_DEBUG_PURLIN_DIALOG", Command);
        Assert.Contains("StartOpenCloseTransaction()", Command);
        Assert.Contains("OpenMode.ForRead", Command);
        Assert.DoesNotContain("OpenMode.ForWrite", Command);
        Assert.DoesNotContain("transaction.Commit", Command);
        Assert.DoesNotContain("LockDocument", Command);
    }

    [Fact]
    public void DebugDialog_HasNoPersistenceOrMaterializationCallSite()
    {
        var forbidden = new[]
        {
            "RoofPurlinLayoutStore.Write",
            "RoofRelativeElevationDatumStore.Write",
            "RoofAutomaticPurlinGeneratedStore.Write",
            "RoofBoundaryIdentityStore.Write",
            "EnsureBoundaryIdentity",
            "RoofAutomaticPurlinMaterializationService",
            "RoofAssemblyGroupSyncService",
            "AppendEntity",
            "AddNewlyCreatedDBObject",
        };
        Assert.All(forbidden, token => Assert.DoesNotContain(token, Command));
        Assert.Contains("RoofPurlinLayoutStore.Read", Command);
        Assert.Contains("RoofRelativeElevationDatumStore.Read", Command);
        Assert.Contains("RoofBoundaryIdentityStore.Read", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewResolver.Resolve", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewSource.Ephemeral", Command);
        Assert.Contains("RoofBoundaryIdentityPreviewSource.Persisted", Command);
    }

    [Fact]
    public void PreviewIdentityAndDiagnosticsRemainReadOnlyAndHostVisible()
    {
        Assert.Contains("ROOF_PURLIN_PREVIEW owner=", Command);
        Assert.Contains("boundaryIdentity=", Command);
        Assert.Contains("result=invalid reason=", Command);
        Assert.Contains("result=blocked reason=", Command);
        Assert.Contains("result=ok", Command);
        Assert.Contains("viewModel.PreviewDiagnosticReason", Command);
        Assert.DoesNotContain("RoofBoundaryIdentityStore.Write", Command);
        Assert.DoesNotContain("EnsureBoundaryIdentity", Command);
    }

    [Fact]
    public void ModalDialogWiresLoadedAndDraftChangesToPreviewRefresh()
    {
        Assert.Contains("AcApplication.ShowModalWindow(window)", Command);
        Assert.Contains("window.PreviewRequested += RefreshPreview", Command);
        Assert.Contains("window.PreviewRequested -= RefreshPreview", Command);
        Assert.Contains("Loaded += Window_Loaded", WindowCodeBehind);
        Assert.Contains("ViewModel.PreviewChanged += ViewModel_PreviewChanged", WindowCodeBehind);
        Assert.Contains("PreviewRequested?.Invoke", WindowCodeBehind);
    }

    [Fact]
    public void PreviewUsesTransientLinesAndDeterministicallyErasesAndDisposesThem()
    {
        Assert.Contains("ShowAutomaticPurlins", Transient);
        Assert.Contains("TransientManager.CurrentTransientManager", Transient);
        Assert.Contains("transientManager.AddTransient", Transient);
        Assert.Contains("EraseTransient", Transient);
        Assert.Contains("drawable.Dispose()", Transient);
        Assert.Contains("DocumentToBeDestroyed", Transient);
        Assert.DoesNotContain("AppendEntity", Transient);
    }

    [Fact]
    public void S6aWindowKeepsPreviewAndCancelAndAddsModeGatedApply()
    {
        Assert.Contains("x:Name=\"PreviewButton\"", Window);
        Assert.Contains("x:Name=\"CancelButton\"", Window);
        Assert.Contains("x:Name=\"ApplyButton\"", Window);
        Assert.Contains("Binding CanApply", Window);
        Assert.Contains("Binding IsProductionEdit", Window);
        Assert.Contains("Value=\"Collapsed\"", Window);
        Assert.Contains("AutomaticPurlin_Apply", Window);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", Window);
    }

    [Fact]
    public void S6aWindowUsesCompactBitmapSchematicWithInteractiveHighlights()
    {
        Assert.Contains("x:Name=\"SchematicCard\"", Window);
        Assert.Contains("x:Name=\"SchematicBaseImage\"", Window);
        Assert.Contains("x:Name=\"SchematicHighlightOverlay\"", Window);
        Assert.Contains("SchematicImagePackUri", Window);
        Assert.Contains("automatic-purlin-roof-section.png", ViewModel);
        Assert.Contains("SchematicRidgeActive", Window);
        Assert.Contains("SchematicRidgeEmphasized", Window);
        Assert.Contains("SchematicAnyIntermediateEnabled", Window);
        Assert.Contains("SchematicIntermediateEmphasized", Window);
        Assert.Contains("RenderOptions.BitmapScalingMode=\"HighQuality\"", Window);
        Assert.Contains("SelectedItem=\"{Binding SelectedRow, Mode=TwoWay}\"", Window);
        Assert.Contains("VerticalScrollBarVisibility=\"Disabled\"", Window);
        Assert.Contains("x:Name=\"RidgeEnabledCheckBox\"", Window);
        Assert.Contains("SetRidgeInteractionActive", WindowCodeBehind);
        Assert.Contains("SetIntermediateInteractionActive", WindowCodeBehind);
        Assert.Contains(
            "<Resource Include=\"UI\\Assets\\Schematics\\*.png\" />",
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "AcKrovy.AutoCAD",
                "AcKrovy.AutoCAD.csproj")));
        Assert.True(File.Exists(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "Assets",
            "Schematics",
            "automatic-purlin-roof-section.png")));
        Assert.DoesNotContain("ShowEaveDistanceCue", Window);
        Assert.DoesNotContain("ShowRidgeDistanceCue", Window);
        Assert.DoesNotContain("ShowVerticalHeightCue", Window);
        Assert.DoesNotContain("ShowSeatingCue", Window);
    }

    [Fact]
    public void S5bWindowExposesSeatingOnlyForApplicableDraftModes()
    {
        Assert.Contains("x:Name=\"SeatingControlsPanel\"", Window);
        Assert.Contains("x:Name=\"HeightModeSeatingStatus\"", Window);
        Assert.Contains("x:Name=\"SeatingModeComboBox\"", Window);
        Assert.Contains("x:Name=\"SeatingValueTextBox\"", Window);
        Assert.Contains("Binding IsSeatingApplicable", Window);
        Assert.Contains("AutomaticPurlin_RoofPlaneElevation", Window);
        Assert.Contains("AutomaticPurlin_SeatingDepth", Window);
        Assert.DoesNotContain("AutomaticPurlin_SeatingMethod", Window);
        Assert.DoesNotContain("AutomaticPurlin_PlanPosition", Window);
        Assert.DoesNotContain("IsEnabled=\"False\"", Window);
    }

    private static string Read(params string[] relativeSegments)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            new[] { root, "src", "AcKrovy.AutoCAD" }.Concat(relativeSegments).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "AcKrovy.AutoCAD")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
