using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class LiveGeometrySourceRefreshCandidateTests
{
    [Fact]
    public void OneRotatedSource_RefreshesOnlyThatSource_NotFullDrawing()
    {
        var findAllCalls = 0;
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: new[] { "noise", "T1" },
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: new[] { "T1" },
            findAllTimberElements: () =>
            {
                findAllCalls++;
                return new[] { "T1", "T2", "T3", "T4", "T5" };
            });

        Assert.Equal(new[] { "T1" }, candidates);
        Assert.Equal(0, findAllCalls);
    }

    [Fact]
    public void FiveRotatedSources_RefreshExactlyThoseFive()
    {
        var findAllCalls = 0;
        var rotated = new[] { "A", "B", "C", "D", "E" };
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: rotated,
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: rotated,
            findAllTimberElements: () =>
            {
                findAllCalls++;
                return rotated.Concat(new[] { "F", "G", "H" }).ToArray();
            });

        Assert.Equal(rotated, candidates);
        Assert.Equal(0, findAllCalls);
    }

    [Fact]
    public void UnrelatedTimber_IsNotIncludedWhenModifiedSetIsPresent()
    {
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: new[] { "T1" },
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: new[] { "T1" },
            findAllTimberElements: () => new[] { "T1", "UNRELATED_1", "UNRELATED_2" });

        Assert.DoesNotContain("UNRELATED_1", candidates);
        Assert.DoesNotContain("UNRELATED_2", candidates);
        Assert.Equal(new[] { "T1" }, candidates);
    }

    [Fact]
    public void RotateFallback_UsesFindAllOnlyWhenNoTimberModsObserved()
    {
        var findAllCalls = 0;
        var all = new[] { "T1", "T2" };
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: Array.Empty<string>(),
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: Array.Empty<string>(),
            findAllTimberElements: () =>
            {
                findAllCalls++;
                return all;
            });

        Assert.Equal(all, candidates);
        Assert.Equal(1, findAllCalls);
    }

    [Fact]
    public void CopySourcePreserving_UsesAppendedIdsNotFindAll()
    {
        var findAllCalls = 0;
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: true,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: new[] { "SRC" },
            appendedIds: new[] { "COPY1", "COPY2" },
            modifiedTimberIds: new[] { "SRC" },
            findAllTimberElements: () =>
            {
                findAllCalls++;
                return new[] { "SRC", "COPY1", "COPY2", "OTHER" };
            });

        Assert.Equal(new[] { "COPY1", "COPY2" }, candidates);
        Assert.Equal(0, findAllCalls);
    }

    [Fact]
    public void MoveWithoutFullRefreshFlag_UsesModifiedTimberSet()
    {
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: false,
            modifiedIds: new[] { "T9", "noise" },
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: new[] { "T9" },
            findAllTimberElements: () => throw new InvalidOperationException("FindAll must not run"));

        Assert.Equal(new[] { "T9" }, candidates);
    }

    [Fact]
    public void DuplicateModifiedTimberIds_AreDeduped()
    {
        var candidates = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
            preserveCopySources: false,
            requiresFullTimberAnnotationRefresh: true,
            modifiedIds: new[] { "T1", "T1" },
            appendedIds: Array.Empty<string>(),
            modifiedTimberIds: new[] { "T1", "T1", "T1" },
            findAllTimberElements: () => new[] { "T1", "T2" });

        Assert.Equal(new[] { "T1" }, candidates);
    }
}
