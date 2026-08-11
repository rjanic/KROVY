using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SettingsAnnotationApplyScopesSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ApplyAllWritesDrawingScaleClearsOverridesAndUsesOneRefreshBatch()
    {
        var commands = Source("src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
        var method = Segment(
            commands,
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements(",
            "private sealed record SettingsDrawingApplyResult(");

        Assert.Contains("TimberAnnotationSettingsApplyScope.AllElements", method);
        Assert.Contains("AutoCadDrawingAnnotationScaleStore(", method);
        Assert.Contains("annotationSettings.ApplyScaleChange", method);
        Assert.Contains(".Write(annotationSettings.ScaleDenominator);", method);
        Assert.Contains("annotationSettings.CreateElementPatch()", method);
        Assert.Contains(
            "TimberAnnotationSettingsChangeRules.ShouldRefreshAllEligible(",
            method);
        Assert.Contains("annotationSettings.PresentationSettingsChanged", method);
        Assert.Equal(1, Count(method, "UpdateLabelsForChangedEntities("));
        Assert.DoesNotContain("ElementLabelService.UpdateAll", method);
    }

    [Fact]
    public void SelectionCompletesBeforeDefaultsAreSavedAndFiltersSmartDistinctIds()
    {
        var commands = Source("src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
        var workflow = Segment(
            commands,
            "private static SettingsApplyResponse ApplySettingsFromWindow(",
            "private static SettingsApplyResponse SettingsResponse(");
        var filter = Segment(
            commands,
            "private static IReadOnlyList<ObjectId> FilterSettingsTimberElementIds(",
            "private enum SettingsSelectionStatus");

        Assert.True(
            workflow.IndexOf("PromptForSettingsEntities(", StringComparison.Ordinal) <
            workflow.IndexOf("TimberElementDefaultProfileStore.Save", StringComparison.Ordinal));
        Assert.Contains("profileAccepted: false", workflow);
        Assert.Contains(".Distinct()", filter);
        Assert.Contains("AutoCadEntityHelpers.IsSupportedTimberGeometry", filter);
        Assert.Contains("metadataStore.TryRead", filter);
    }

    [Fact]
    public void EveryCreationPathUsesTheCentralDefaultThatIncludesScale()
    {
        var defaults = Source("src", "AcKrovy.Core", "Services", "TimberElementDefaults.cs");
        var commands = Source("src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");
        var post = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "PostFootprintAssignmentWorkflow.cs");
        var copyService = Source(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberElementCopyInitializationService.cs");

        Assert.Contains(
            "AnnotationScaleDenominatorOverride = defaults.AnnotationScaleDenominator",
            defaults);
        Assert.Contains("TimberElementDefaults.For(elementType, defaultProfile)", commands);
        Assert.Contains("TimberElementDefaults.For(TimberElementType.Custom, defaultProfile)", commands);
        Assert.Contains("TimberElementDefaults.For(TimberElementType.Post, defaultProfile)", post);
        Assert.DoesNotContain("TimberElementDefaults.For", copyService);
        Assert.DoesNotContain("metadataStore.Write", copyService);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
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
