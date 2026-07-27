using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class EditCommandSourceContractTests
{
    private static readonly string CommandsSource = File.ReadAllText(Path.Combine(
        RepositoryRoot(),
        "src",
        "AcKrovy.AutoCAD",
        "Commands",
        "AcKrovyCommands.cs"));

    [Fact]
    public void Edit_IsRegisteredForPickFirstAndSelectSimilarPreservesItsResult()
    {
        Assert.Contains(
            "[CommandMethod(AcKrovyCommandNames.Edit, CommandFlags.Modal | CommandFlags.UsePickSet)]",
            CommandsSource);
        Assert.Contains(
            "CommandFlags.Modal | CommandFlags.UsePickSet | CommandFlags.Redraw)]",
            CommandsSource);
    }

    [Fact]
    public void Edit_ExitsBeforeMutatingWorkflowWhenNothingWasRequested()
    {
        var edit = Segment(
            "[CommandMethod(AcKrovyCommandNames.Edit",
            "[CommandMethod(AcKrovyCommandNames.FlipSlope");
        var guard = edit.IndexOf(
            "!TimberElementEditRules.HasRequestedChange",
            StringComparison.Ordinal);
        var transaction = edit.IndexOf(
            "StartTransaction()",
            guard,
            StringComparison.Ordinal);

        Assert.True(guard >= 0);
        Assert.True(transaction > guard);
        Assert.Contains("\"Command_Edit_NoChanges\"", edit);
    }

    [Fact]
    public void Edit_OpensCandidatesForReadAndWritesOnlyEffectiveChanges()
    {
        var edit = Segment(
            "[CommandMethod(AcKrovyCommandNames.Edit",
            "[CommandMethod(AcKrovyCommandNames.FlipSlope");

        Assert.Contains("OpenMode.ForRead", edit);
        Assert.DoesNotContain("OpenMode.ForWrite", edit);
        Assert.Contains("TimberElementEditRules.TryCreateEffectiveChange", edit);
        Assert.Contains("metadataStore.Write(entity, merged);", edit);
        Assert.Contains("if (changedIds.Count > 0)", edit);
        Assert.Contains("UpdateLabelsForChangedEntities(", edit);
    }

    [Fact]
    public void EditSelection_UsesImpliedItemsBeforeManualPromptAndFiltersInvalidItems()
    {
        var resolver = Segment(
            "private static EditSelectionResult ResolveEditSelection(",
            "private static SettingsSelectionResult PromptForSettingsEntities(");
        var selectImplied = resolver.IndexOf("editor.SelectImplied()", StringComparison.Ordinal);
        var evaluate = resolver.IndexOf("TimberEditSelectionRules.Evaluate", StringComparison.Ordinal);
        var manualPrompt = resolver.IndexOf("PromptForManualEntities", StringComparison.Ordinal);

        Assert.True(selectImplied >= 0);
        Assert.True(evaluate > selectImplied);
        Assert.True(manualPrompt > evaluate);
        Assert.Contains("decision.ValidItems", resolver);
        Assert.Contains("decision.RejectedItems", resolver);
        Assert.Contains("OpenMode.ForRead", resolver);
        Assert.DoesNotContain("OpenMode.ForWrite", resolver);
    }

    private static string Segment(string start, string end)
    {
        var startIndex = CommandsSource.IndexOf(start, StringComparison.Ordinal);
        var endIndex = CommandsSource.IndexOf(
            end,
            startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return CommandsSource.Substring(startIndex, endIndex - startIndex);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "AcKrovy.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ??
            throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
