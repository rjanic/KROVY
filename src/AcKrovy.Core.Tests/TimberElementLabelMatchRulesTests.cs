using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelMatchRulesTests
{
    [Fact]
    public void SelectLabelForUpsert_OldElementIdSameSourceHandle_SelectsExistingLabel()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K8",
            previousElementId: "W3",
            new[]
            {
                Label("label-1", "W3", "H1"),
            },
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("label-1", result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    [Fact]
    public void SelectLabelForUpsert_TwoLabelsWithSameSourceHandle_SelectsOneAndMarksDuplicate()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K8",
            previousElementId: "W3",
            new[]
            {
                Label("old", "W3", "H1"),
                Label("current", "K8", "H1"),
            },
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("current", result.LabelKeyToUpdate);
        Assert.Equal(new[] { "old" }, result.LabelKeysToDelete);
    }

    [Fact]
    public void SelectLabelForUpsert_LabelWithDifferentSourceHandle_IsNotTouched()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K8",
            previousElementId: "W3",
            new[]
            {
                Label("other", "K8", "H2"),
            },
            currentElementOwnerCount: 2,
            previousElementOwnerCount: 0);

        Assert.Null(result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    [Fact]
    public void SelectLabelForUpsert_LabelWithoutSourceHandle_FallsBackToPreviousElementIdWhenUnique()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K8",
            previousElementId: "W3",
            new[]
            {
                Label("legacy", "W3", string.Empty),
            },
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("legacy", result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    [Fact]
    public void SelectLabelForUpsert_LabelWithoutSourceHandle_DoesNotFallbackWhenElementIdIsAmbiguous()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K8",
            previousElementId: "W3",
            new[]
            {
                Label("legacy-1", "W3", string.Empty),
                Label("legacy-2", "W3", string.Empty),
            },
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Null(result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    [Fact]
    public void SelectLabelForUpsert_LabelWithoutSourceHandle_DoesNotFallbackToPreviousElementIdWhenAnotherOwnerStillExists()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "K2",
            previousElementId: "K1",
            new[]
            {
                Label("original-label", "K1", string.Empty),
            },
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.Null(result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    private static TimberElementLabelCandidate Label(string key, string elementId, string sourceHandle) =>
        new()
        {
            LabelKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
        };
}
