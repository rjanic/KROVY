using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedG4CompositeMatchRulesTests
{
    [Fact]
    public void SelectComposite_SourceHandleMatch_WinsOverSharedElementId()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "COPY",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("orig-leader", "K1", "ORIGINAL", "group-a"),
                Frame("orig-frame", "K1", "ORIGINAL", "group-a"),
                Item("orig-item", "K1", "ORIGINAL", "group-a"),
                Leader("copy-leader", "K1", "COPY", "group-b"),
                Frame("copy-frame", "K1", "COPY", "group-b"),
                Item("copy-item", "K1", "COPY", "group-b"),
            ],
            currentElementOwnerCount: 2,
            previousElementOwnerCount: 2);

        Assert.Equal("copy-leader", result.LeaderKey);
        Assert.Equal("copy-frame", result.FrameKey);
        Assert.Equal("copy-item", result.ItemCodeKey);
        Assert.Equal("group-b", result.AnnotationGroupId);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void SelectComposite_SharedElementIdWithoutSourceHandle_DoesNotStealSiblingComposite()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "COPY",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("orig-leader", "K1", "ORIGINAL", "group-a"),
                Frame("orig-frame", "K1", "ORIGINAL", "group-a"),
                Item("orig-item", "K1", "ORIGINAL", "group-a"),
            ],
            currentElementOwnerCount: 2,
            previousElementOwnerCount: 2);

        Assert.False(result.HasAnySelectedEntity);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    [Trait("Feature", "CopySourcePreservation")]
    public void CopySafeMatch_NeverUsesElementIdFallbackForDifferentSourceHandle()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "NEW-COPY",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("source-leader", "K1", "ORIGINAL", "group-a"),
                Frame("source-frame", "K1", "ORIGINAL", "group-a"),
                Item("source-item", "K1", "ORIGINAL", "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1,
            allowElementIdFallback: false);

        Assert.False(result.HasAnySelectedEntity);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void SelectComposite_UniqueOwnerElementIdFallback_ReclaimsLegacyEmptySourceHandle()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H1",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("legacy-leader", "K1", string.Empty, "group-a"),
                Frame("legacy-frame", "K1", string.Empty, "group-a"),
                Item("legacy-item", "K1", string.Empty, "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.Equal("legacy-leader", result.LeaderKey);
        Assert.Equal("legacy-frame", result.FrameKey);
        Assert.Equal("legacy-item", result.ItemCodeKey);
        Assert.Equal("group-a", result.AnnotationGroupId);
    }

    [Fact]
    public void SelectComposite_ElementIdFallback_RefusesWhenAnnotationsSpanMultipleSourceHandles()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "MISSING",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("a-leader", "K1", "H-A", "group-a"),
                Frame("a-frame", "K1", "H-A", "group-a"),
                Item("a-item", "K1", "H-A", "group-a"),
                Leader("b-leader", "K1", "H-B", "group-b"),
                Frame("b-frame", "K1", "H-B", "group-b"),
                Item("b-item", "K1", "H-B", "group-b"),
            ],
            // Synthetic: one timber owner but annotations already split across
            // two handles — must not assemble a mixed composite.
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.False(result.HasAnySelectedEntity);
    }

    [Fact]
    public void SelectComposite_SameSourceHandleDuplicates_KeepOnePerRoleAndMarkExtras()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H1",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("leader-keep", "K1", "H1", "group-a"),
                Leader("leader-dup", "K1", "H1", "group-a"),
                Frame("frame-keep", "K1", "H1", "group-a"),
                Item("item-keep", "K1", "H1", "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.True(
            result.LeaderKey is "leader-keep" or "leader-dup");
        Assert.Single(result.EntityKeysToDelete);
        Assert.Contains(
            result.LeaderKey == "leader-keep" ? "leader-dup" : "leader-keep",
            result.EntityKeysToDelete);
        Assert.Equal("frame-keep", result.FrameKey);
        Assert.Equal("item-keep", result.ItemCodeKey);
    }

    [Fact]
    public void SelectComposite_PreviousElementIdFallback_OnlyWhenPreviousOwnerGone()
    {
        var reclaimed = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H-NEW",
            currentElementId: "K2",
            previousElementId: "K1",
            candidates:
            [
                Leader("old-leader", "K1", string.Empty, "group-a"),
                Frame("old-frame", "K1", string.Empty, "group-a"),
                Item("old-item", "K1", string.Empty, "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("old-leader", reclaimed.LeaderKey);

        var blocked = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H-NEW",
            currentElementId: "K2",
            previousElementId: "K1",
            candidates:
            [
                Leader("old-leader", "K1", string.Empty, "group-a"),
                Frame("old-frame", "K1", string.Empty, "group-a"),
                Item("old-item", "K1", string.Empty, "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.False(blocked.HasAnySelectedEntity);
    }

    [Fact]
    public void SelectComposite_DoesNotMixRolesAcrossSourceHandlesWhenSourceMatchesPartial()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "COPY",
            currentElementId: "K1",
            previousElementId: "K1",
            candidates:
            [
                Leader("copy-leader", "K1", "COPY", "group-b"),
                // Missing frame/item on COPY — sibling still complete.
                Frame("orig-frame", "K1", "ORIGINAL", "group-a"),
                Item("orig-item", "K1", "ORIGINAL", "group-a"),
            ],
            currentElementOwnerCount: 2,
            previousElementOwnerCount: 2);

        Assert.Equal("copy-leader", result.LeaderKey);
        Assert.Null(result.FrameKey);
        Assert.Null(result.ItemCodeKey);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void SelectComposite_ThreeSameKindSiblings_EachKeepsOwnCompositeOnRefresh()
    {
        var candidates = new List<TimberFramedG4CompositeCandidate>();
        for (var index = 1; index <= 3; index++)
        {
            var handle = $"H{index}";
            var group = $"group-{index}";
            candidates.Add(Leader($"leader-{index}", "K1", handle, group));
            candidates.Add(Frame($"frame-{index}", "K1", handle, group));
            candidates.Add(Item($"item-{index}", "K1", handle, group));
        }

        for (var index = 1; index <= 3; index++)
        {
            var handle = $"H{index}";
            var first = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
                sourceHandle: handle,
                currentElementId: "K1",
                previousElementId: "K1",
                candidates,
                currentElementOwnerCount: 3,
                previousElementOwnerCount: 3);
            var second = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
                sourceHandle: handle,
                currentElementId: "K1",
                previousElementId: "K1",
                candidates,
                currentElementOwnerCount: 3,
                previousElementOwnerCount: 3);

            Assert.Equal($"leader-{index}", first.LeaderKey);
            Assert.Equal($"frame-{index}", first.FrameKey);
            Assert.Equal($"item-{index}", first.ItemCodeKey);
            Assert.Equal($"group-{index}", first.AnnotationGroupId);
            Assert.Empty(first.EntityKeysToDelete);
            Assert.Equal(first.LeaderKey, second.LeaderKey);
            Assert.Equal(first.FrameKey, second.FrameKey);
            Assert.Equal(first.ItemCodeKey, second.ItemCodeKey);
        }
    }

    private static TimberFramedG4CompositeCandidate Leader(
        string key,
        string elementId,
        string sourceHandle,
        string? groupId) =>
        Candidate(
            key,
            elementId,
            sourceHandle,
            TimberMainAnnotationComponentRole.CircleLeaderLine,
            groupId);

    private static TimberFramedG4CompositeCandidate Frame(
        string key,
        string elementId,
        string sourceHandle,
        string? groupId) =>
        Candidate(
            key,
            elementId,
            sourceHandle,
            TimberMainAnnotationComponentRole.CircleFrame,
            groupId);

    private static TimberFramedG4CompositeCandidate Item(
        string key,
        string elementId,
        string sourceHandle,
        string? groupId) =>
        Candidate(
            key,
            elementId,
            sourceHandle,
            TimberMainAnnotationComponentRole.CircleText,
            groupId);

    private static TimberFramedG4CompositeCandidate Candidate(
        string key,
        string elementId,
        string sourceHandle,
        TimberMainAnnotationComponentRole role,
        string? groupId) =>
        new()
        {
            EntityKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
            ComponentRole = role,
            AnnotationGroupId = groupId,
        };
}
