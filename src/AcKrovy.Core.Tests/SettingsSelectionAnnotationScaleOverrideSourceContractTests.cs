using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SettingsSelectionAnnotationScaleOverrideSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void AllThreeButtonsUseOneValidatedAnnotationSettingsRequest()
    {
        var window = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");
        var apply = Segment(
            window,
            "private void ApplySettings(",
            "private static string CreateLayerProfileFingerprint(");

        Assert.Contains("TimberAnnotationSettingsRequest? annotationSettings", apply);
        Assert.Contains("new TimberAnnotationSettingsRequest(", apply);
        Assert.Contains("selectedScaleDenominator", apply);
        Assert.Contains("annotationApplyScope.Value", apply);
    }

    [Fact]
    public void FooterScopesReplaceTheRemovedScaleOnlyAction()
    {
        var window = Source(
            "src",
            "AcKrovy.AutoCAD",
            "UI",
            "LayerSettingsWindow.xaml.cs");
        var handlers = Segment(
            window,
            "private void SaveNewElements_Click(",
            "private void ApplySettings(");

        Assert.Contains("TimberAnnotationSettingsApplyScope.NewElementsOnly", handlers);
        Assert.Contains("TimberAnnotationSettingsApplyScope.SelectedElements", handlers);
        Assert.Contains("TimberAnnotationSettingsApplyScope.AllElements", handlers);
        Assert.DoesNotContain("ApplyDrawingAnnotationScale", window);
    }

    [Fact]
    public void SelectionBatchAppliesPatchWritesOnlyChangesAndRefreshesChangedIds()
    {
        var commands = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");
        var apply = Segment(
            commands,
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements(",
            "private sealed record SettingsDrawingApplyResult(");

        Assert.Contains("targetIds.Distinct().ToList()", apply);
        Assert.Contains("AutoCadObjectIdAccess.TryGetObject<Entity>(", apply);
        Assert.Contains("AutoCadEntityHelpers.IsSupportedTimberGeometry(entity)", apply);
        Assert.Contains("TimberAnnotationSettingsApplicator.Apply(", apply);
        Assert.Contains("annotationSettings.CreateElementPatch()", apply);
        Assert.Contains("var metadataChanged = updatedData != data;", apply);
        Assert.Contains("if (metadataChanged)", apply);
        Assert.Contains("metadataStore.Write(entity, updatedData);", apply);
        Assert.Contains("changedIds.Add(id);", apply);
        Assert.Contains("UpdateLabelsForChangedEntities(", apply);
        Assert.DoesNotContain("ElementLabelService.UpdateAll", apply);
    }

    [Fact]
    public void SelectionFailureRollsBackTransactionAndReadOnlyOutcomesDoNotWrite()
    {
        var commands = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Commands",
            "AcKrovyCommands.cs");
        var workflow = Segment(
            commands,
            "private static SettingsApplyResponse ApplySettingsFromWindow(",
            "private static SettingsApplyResponse SettingsResponse(");
        var apply = Segment(
            commands,
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements(",
            "private sealed record SettingsDrawingApplyResult(");

        Assert.True(
            workflow.IndexOf("PromptForSettingsEntities(", StringComparison.Ordinal) <
            workflow.IndexOf("TimberElementDefaultProfileStore.Save", StringComparison.Ordinal));
        Assert.Contains("selection.Status != SettingsSelectionStatus.Selected", workflow);
        Assert.Contains("success: false", workflow);
        Assert.Contains("throw new InvalidOperationException(", apply);
        Assert.Contains("var metadataChanged = updatedData != data;", apply);
    }

    [Fact]
    public void CanonicalMetadataWriteUpgradesOnlyOnActualWrite()
    {
        var store = Source(
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "ElementDataStore.cs");

        Assert.Contains("TimberElementDataVersioning.PrepareForWrite(data)", store);
        Assert.DoesNotContain("PrepareForWrite", Segment(
            store,
            "public static bool TryRead(",
            "public static void Write("));
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
