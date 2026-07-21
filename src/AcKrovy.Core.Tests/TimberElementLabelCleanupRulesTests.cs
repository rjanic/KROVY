using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelCleanupRulesTests
{
    [Fact]
    public void SelectDuplicateLabelKeysToDelete_SameElementIdDifferentSourceHandlesKeepsBoth()
    {
        var result = Select(
            new[] { "AAA", "BBB" },
            Label("label-a", "K1", "AAA"),
            Label("label-b", "K1", "BBB"));

        Assert.Empty(result);
    }

    [Fact]
    public void SelectDuplicateLabelKeysToDelete_CloneLabelWithSameExistingSourceHandleIsDeleted()
    {
        var result = Select(
            new[] { "AAA", "BBB" },
            Label("original", "K1", "AAA"),
            Label("clone", "K1", "AAA"),
            Label("new", "K2", "BBB"));

        Assert.Equal(new[] { "clone" }, result);
    }

    [Fact]
    public void SelectDuplicateLabelKeysToDelete_OriginalLabelIsKept()
    {
        var result = Select(
            new[] { "AAA" },
            Label("original", "K1", "AAA"),
            Label("clone-1", "K1", "AAA"),
            Label("clone-2", "K1", "AAA"));

        Assert.Equal(new[] { "clone-1", "clone-2" }, result);
    }

    [Fact]
    public void SelectDuplicateLabelKeysToDelete_NewEntityLabelWithOwnSourceHandleIsKept()
    {
        var result = Select(
            new[] { "AAA", "BBB" },
            Label("original", "K1", "AAA"),
            Label("new", "K2", "BBB"));

        Assert.Empty(result);
    }

    [Fact]
    public void SelectDuplicateLabelKeysToDelete_RepeatedCleanupIsIdempotent()
    {
        var first = Select(
            new[] { "AAA", "BBB" },
            Label("original", "K1", "AAA"),
            Label("clone", "K1", "AAA"),
            Label("new", "K2", "BBB"));
        var second = Select(
            new[] { "AAA", "BBB" },
            Label("original", "K1", "AAA"),
            Label("new", "K2", "BBB"));

        Assert.Equal(new[] { "clone" }, first);
        Assert.Empty(second);
    }

    [Fact]
    public void SelectLabelsWithoutExistingSourceHandleToDelete_RemovesOnlyStaleInsertedLabel()
    {
        var result = TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
            new[]
            {
                Label("manual-like", "K1", string.Empty),
                Label("stale-clone", "K1", "OLD"),
                Label("current", "K1", "NEW"),
            },
            new[] { "NEW" });

        Assert.Equal(new[] { "stale-clone" }, result);
    }

    [Fact]
    public void CopiedPostAnnotationCleanupKeepsOneGlyphForEachPhysicalSourceHandle()
    {
        var firstPass = Select(
            new[] { "POST-OLD", "POST-NEW" },
            Label("original-post-marker", "S1", "POST-OLD"),
            Label("copied-old-binding", "S1", "POST-OLD"),
            Label("refreshed-new-binding", "S2", "POST-NEW"));
        var secondPass = Select(
            new[] { "POST-OLD", "POST-NEW" },
            Label("original-post-marker", "S1", "POST-OLD"),
            Label("refreshed-new-binding", "S2", "POST-NEW"));

        Assert.Equal(new[] { "copied-old-binding" }, firstPass);
        Assert.Empty(secondPass);
    }

    private static IReadOnlyList<string> Select(
        IReadOnlyCollection<string> existingTimberSourceHandles,
        params TimberElementLabelCandidate[] labels) =>
        TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(labels, existingTimberSourceHandles);

    private static TimberElementLabelCandidate Label(string key, string elementId, string sourceHandle) =>
        new()
        {
            LabelKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
        };
}
