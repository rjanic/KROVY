using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofPersistenceSourceContractTests
{
    private static readonly string Workflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofCommandWorkflow.cs");
    private static readonly string Window = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "UI", "HipRoofPreviewWindow.xaml");
    private static readonly string WindowCode = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "UI", "HipRoofPreviewWindow.xaml.cs");

    [Fact]
    public void HipSlopeTextBoxUsesFixedMetricsWithoutStarCompression()
    {
        Assert.Contains("x:Key=\"HipSlopeTextBoxStyle\"", Window);
        Assert.Contains("Style=\"{StaticResource HipSlopeTextBoxStyle}\"", Window);
        Assert.Contains("SizeToContent=\"Height\"", Window);
        Assert.Contains("Property=\"Height\" Value=\"{DynamicResource SettingsControlHeight}\"", Window);
        Assert.Contains("Property=\"MinHeight\" Value=\"{DynamicResource SettingsControlHeight}\"", Window);
        Assert.Contains("Property=\"MaxHeight\" Value=\"{DynamicResource SettingsControlHeight}\"", Window);
        Assert.Contains("Property=\"Padding\" Value=\"10,0\"", Window);
        Assert.Contains("x:Name=\"PART_ContentHost\"", Window);
        Assert.Contains("VerticalAlignment=\"Center\"", Window);
        Assert.Contains("Property=\"TextAlignment\" Value=\"Right\"", Window);
        Assert.DoesNotContain("<RowDefinition Height=\"*\"", Window);
        Assert.Contains("<RowDefinition Height=\"Auto\"", Window);
    }

    [Fact]
    public void HipCreateRequiresExplicitApplyBeforeEnteringPersistence()
    {
        var apply = Segment(
            Workflow,
            "case HipRoofPreviewDialogAction.Apply:",
            "default:");
        Assert.Contains("RoofDefinitionPersistence.Create(", apply);
        Assert.Contains("TryPersist(document, ownerId, data", apply);
        Assert.Contains("Command_Roof_PersistedAndDisplaySaved", apply);
        Assert.Contains("x:Name=\"ApplyButton\"", Window);
        Assert.Contains("Click=\"ApplyButton_Click\"", Window);
        Assert.Contains("HipRoofPreviewDialogAction.Apply", WindowCode);
    }

    [Fact]
    public void PreviewAndCancelCannotEnterWriteScope()
    {
        var preview = Segment(
            Workflow,
            "case HipRoofPreviewDialogAction.Preview:",
            "case HipRoofPreviewDialogAction.Apply:");
        Assert.Contains("ShowPreview(", preview);
        Assert.DoesNotContain("RoofFaceRafterLayoutService", preview);
        Assert.DoesNotContain("AutoCadRoofRafterSpacingStore", preview);
        Assert.DoesNotContain("TryPersist", preview);
        Assert.DoesNotContain("RoofDefinitionStore.Write", preview);
        Assert.DoesNotContain("OpenMode.ForWrite", preview);
        Assert.DoesNotContain("transaction.Commit", preview);

        var cancel = Segment(WindowCode, "private void CancelButton_Click", "private void ApplyButton_Click");
        Assert.Contains("HipRoofPreviewDialogAction.Cancel", cancel);
        Assert.DoesNotContain("Persistence", cancel);
        Assert.DoesNotContain("XData", cancel);
    }

    [Fact]
    public void HipUsesSharedAtomicDefinitionWriteAndPermanentDisplay()
    {
        var persist = Segment(
            Workflow,
            "private static bool TryPersist",
            "private static RoofDisplayInspection InspectDisplay");
        var writeIndex = persist.IndexOf("RoofDefinitionStore.Write(owner, transaction, data)", StringComparison.Ordinal);
        var wireframeIndex = persist.IndexOf("RoofWireframe.Create(restored.Geometry, sourceElevation)", StringComparison.Ordinal);
        var rebuildIndex = persist.IndexOf("RoofDisplayService.Rebuild(", StringComparison.Ordinal);
        var commitIndex = persist.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(writeIndex >= 0);
        Assert.True(wireframeIndex > writeIndex);
        Assert.True(rebuildIndex > wireframeIndex);
        Assert.True(commitIndex > rebuildIndex);
        Assert.DoesNotContain("restored.Geometry is not HipRoofGeometry", persist);
        Assert.Equal(1, Count(persist, "transaction.Commit();"));
        Assert.Contains("using var documentLock = document.LockDocument()", persist);
        Assert.Contains("using var transaction", persist);
        Assert.Contains("if (RoofDefinitionStore.Read(owner).Exists)", persist);
        Assert.Contains("return true;", persist);
        Assert.Contains("failureMessageKey = \"Command_Roof_DisplayFutureSchema\"", persist);
        Assert.Contains("return false;", persist);
    }

    [Fact]
    public void StoredHipReloadsThroughSharedDisplayLifecycleWithoutRidgeDirection()
    {
        var stored = Segment(Workflow, "if (storedDefinition.Exists)", "RunCreationDialog(");
        var restoreIndex = stored.IndexOf("RoofDefinitionPersistence.Restore", StringComparison.Ordinal);
        var previewIndex = stored.IndexOf("ShowPreview(document, restored.Geometry", StringComparison.Ordinal);
        var wireframeIndex = stored.IndexOf("RoofWireframe.Create(restored.Geometry, sourceElevation)", StringComparison.Ordinal);
        var inspectIndex = stored.IndexOf("InspectDisplay(", StringComparison.Ordinal);
        Assert.True(restoreIndex >= 0);
        Assert.True(previewIndex > restoreIndex);
        Assert.True(wireframeIndex > previewIndex);
        Assert.True(inspectIndex > wireframeIndex);
        Assert.Contains("reload success token=Hip", stored);
        Assert.Contains("InspectDisplay(", stored);
        Assert.Contains("RoofDisplayLifecycleKind.Current", stored);
        Assert.DoesNotContain("GableRoofGeometryWindow", stored);
        Assert.DoesNotContain("TryPromptOrientationDirection", stored);
    }

    [Fact]
    public void HipDiagnosticsRemainDebugOnlyAndReportPersistenceAndReload()
    {
        Assert.Contains("#if DEBUG", Workflow);
        Assert.Contains("persistence begin kind={data.Kind} token=Hip", Workflow);
        Assert.Contains("persistence success token=Hip", Workflow);
        Assert.Contains("reload success token=Hip", Workflow);
        Assert.DoesNotContain("Editor.WriteMessage(\"[AK_ROOF_HIP]", Workflow);
    }

    private static string Segment(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static int Count(string value, string token) =>
        (value.Length - value.Replace(token, string.Empty, StringComparison.Ordinal).Length) /
        token.Length;
}
