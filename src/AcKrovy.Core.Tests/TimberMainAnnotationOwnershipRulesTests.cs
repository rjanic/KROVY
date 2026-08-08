using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberMainAnnotationOwnershipRulesTests
{
    [Fact]
    public void TypeChange_SourceHandleMatch_KeepsOwnedCompositeWhenElementIdChanges()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "K1",
            candidates:
            [
                Leader("leader", "K1", "H1", "group-a"),
                Frame("frame", "K1", "H1", "group-a"),
                Item("item", "K1", "H1", "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("leader", result.LeaderKey);
        Assert.Equal("frame", result.FrameKey);
        Assert.Equal("item", result.ItemCodeKey);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void TypeChange_RafterToCollarTie_SourceHandleMatch_KeepsOwnedComposite()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H1",
            currentElementId: TimberElementIdentityRules.CreateElementId(
                TimberElementIdentityPrefixes.GetPrefix(TimberElementType.CollarTie),
                1),
            previousElementId: TimberElementIdentityRules.CreateElementId(
                TimberElementIdentityPrefixes.GetPrefix(TimberElementType.Rafter),
                1),
            candidates:
            [
                Leader("leader", "K1", "H1", "group-a"),
                Frame("frame", "K1", "H1", "group-a"),
                Item("item", "K1", "H1", "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("leader", result.LeaderKey);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void TypeChange_RafterToTieBeam_SourceHandleMatch_KeepsOwnedComposite()
    {
        var result = TimberFramedG4CompositeMatchRules.SelectCompositeForUpsert(
            sourceHandle: "H1",
            currentElementId: TimberElementIdentityRules.CreateElementId(
                TimberElementIdentityPrefixes.GetPrefix(TimberElementType.TieBeam),
                1),
            previousElementId: TimberElementIdentityRules.CreateElementId(
                TimberElementIdentityPrefixes.GetPrefix(TimberElementType.Rafter),
                1),
            candidates:
            [
                Leader("leader", "K1", "H1", "group-a"),
                Frame("frame", "K1", "H1", "group-a"),
                Item("item", "K1", "H1", "group-a"),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("item", result.ItemCodeKey);
        Assert.Empty(result.EntityKeysToDelete);
    }

    [Fact]
    public void TypeChange_PrimaryLabel_SourceHandleMatchWinsOverNewElementId()
    {
        var result = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "K1",
            candidates:
            [
                new TimberElementLabelCandidate
                {
                    LabelKey = "primary",
                    ElementId = "K1",
                    SourceHandle = "H1",
                    ComponentRole = TimberMainAnnotationComponentRole.Primary,
                },
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);

        Assert.Equal("primary", result.LabelKeyToUpdate);
        Assert.Empty(result.LabelKeysToDelete);
    }

    [Fact]
    public void TypeChange_DoesNotCreateSecondWhenSourceHandleAlreadyOwnsAnnotation()
    {
        var first = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "K1",
            candidates:
            [
                Candidate("old-primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);
        var second = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "KL1",
            candidates:
            [
                Candidate("old-primary", "KL1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.Equal("old-primary", first.LabelKeyToUpdate);
        Assert.Equal("old-primary", second.LabelKeyToUpdate);
        Assert.Empty(first.LabelKeysToDelete);
        Assert.Empty(second.LabelKeysToDelete);
    }

    [Fact]
    public void TypeChange_RafterToCollarTie_DropsDuplicateRolesPreferringNewElementId()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
            [
                Candidate("old-item", "K1", "H1", TimberMainAnnotationComponentRole.CircleText, "old"),
                Candidate("new-item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "new"),
                Candidate("old-primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
                Candidate("new-primary", "KL1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "KL1");

        Assert.Equal(
            new[] { "old-item", "old-primary" }.OrderBy(key => key),
            deleted.OrderBy(key => key));
    }

    [Fact]
    public void TypeChange_RafterToTieBeam_DropsStaleElementIdAfterPreferredExists()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectStaleElementIdKeysToDelete(
            [
                Candidate("g4-item", "VT1", "H1", TimberMainAnnotationComponentRole.CircleText, "g1"),
                Candidate("g4-frame", "VT1", "H1", TimberMainAnnotationComponentRole.CircleFrame, "g1"),
                Candidate("g4-leader", "VT1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine, "g1"),
                Candidate("primary", "VT1", "H1", TimberMainAnnotationComponentRole.Primary),
                Candidate("stale-item", "K1", "H1", TimberMainAnnotationComponentRole.CircleText, "stale"),
                Candidate("stale-primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
                Candidate("sibling", "K2", "H2", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "VT1");

        Assert.Equal(
            new[] { "stale-item", "stale-primary" }.OrderBy(key => key),
            deleted.OrderBy(key => key));
    }

    [Fact]
    public void TypeChange_StaleElementId_DoesNotRunWhenPreferredNotYetPresent()
    {
        // Mid-flight Combined: G4 already KL1, Primary still K1 — stale cleanup
        // must wait until preferred exists on the role being protected... actually
        // preferred IS present on G4, so stale would delete Primary. That is why
        // host wiring runs stale cleanup only after Primary upsert.
        var deleted = TimberMainAnnotationOwnershipRules.SelectStaleElementIdKeysToDelete(
            [
                Candidate("g4-item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "g1"),
                Candidate("primary-still-old", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "KL1");

        Assert.Equal(new[] { "primary-still-old" }, deleted);
    }

    [Fact]
    public void TypeChange_RepeatedRefresh_IdempotentWhenAlreadyOnNewElementId()
    {
        var candidates = new[]
        {
            Candidate("item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "g1"),
            Candidate("frame", "KL1", "H1", TimberMainAnnotationComponentRole.CircleFrame, "g1"),
            Candidate("leader", "KL1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine, "g1"),
            Candidate("primary", "KL1", "H1", TimberMainAnnotationComponentRole.Primary),
        };

        var first = OwnershipCleanup(candidates, "KL1");
        var second = OwnershipCleanup(candidates, "KL1");

        Assert.Empty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void TypeChange_DoesNotStealSiblingSourceHandleAnnotations()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectStaleElementIdKeysToDelete(
            [
                Candidate("a-item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "a"),
                Candidate("b-item", "K1", "H2", TimberMainAnnotationComponentRole.CircleText, "b"),
                Candidate("b-primary", "K1", "H2", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "KL1");

        Assert.Empty(deleted);
    }

    [Fact]
    public void SupersededLegacyFramedItem_DeletedWhenG4ExistsForSameSourceHandle()
    {
        var deleted =
            TimberMainAnnotationOwnershipRules.SelectSupersededLegacyFramedLeaderKeys(
            [
                Candidate("g4-item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "g1"),
                Candidate("g4-frame", "KL1", "H1", TimberMainAnnotationComponentRole.CircleFrame, "g1"),
                Candidate("g4-leader", "KL1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine, "g1"),
                Candidate("legacy-framed", "K1", "H1", TimberMainAnnotationComponentRole.FramedItem),
                Candidate("primary-dim", "KL1", "H1", TimberMainAnnotationComponentRole.Primary),
            ]);

        Assert.Equal(new[] { "legacy-framed" }, deleted);
    }

    [Fact]
    public void ExtraG4Group_DeletedKeepingCanonicalForSourceHandle()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectExtraG4GroupKeysToDelete(
            [
                Candidate("a-leader", "KL1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine, "keep"),
                Candidate("a-frame", "KL1", "H1", TimberMainAnnotationComponentRole.CircleFrame, "keep"),
                Candidate("a-item", "KL1", "H1", TimberMainAnnotationComponentRole.CircleText, "keep"),
                Candidate("b-leader", "K1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine, "drop"),
                Candidate("b-frame", "K1", "H1", TimberMainAnnotationComponentRole.CircleFrame, "drop"),
                Candidate("b-item", "K1", "H1", TimberMainAnnotationComponentRole.CircleText, "drop"),
            ],
            preferredElementId: "KL1");

        Assert.Equal(
            new[] { "b-leader", "b-frame", "b-item" }.OrderBy(key => key),
            deleted.OrderBy(key => key));
    }

    [Fact]
    public void ExtraG4Group_WithoutGroupIds_DoesNotDeleteUngroupedCompositeParts()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectExtraG4GroupKeysToDelete(
            [
                Candidate("leader", "K1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine),
                Candidate("frame", "K1", "H1", TimberMainAnnotationComponentRole.CircleFrame),
                Candidate("item", "K1", "H1", TimberMainAnnotationComponentRole.CircleText),
            ],
            preferredElementId: "K1");

        Assert.Empty(deleted);
    }

    [Fact]
    public void UngroupedDuplicates_SurplusRolePrefersNewElementIdAfterTypeChange()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
            [
                Candidate("old-leader", "K1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine),
                Candidate("new-leader", "VT1", "H1", TimberMainAnnotationComponentRole.CircleLeaderLine),
                Candidate("old-frame", "K1", "H1", TimberMainAnnotationComponentRole.CircleFrame),
                Candidate("new-frame", "VT1", "H1", TimberMainAnnotationComponentRole.CircleFrame),
                Candidate("old-item", "K1", "H1", TimberMainAnnotationComponentRole.CircleText),
                Candidate("new-item", "VT1", "H1", TimberMainAnnotationComponentRole.CircleText),
            ],
            preferredElementId: "VT1");

        Assert.Equal(
            new[] { "old-leader", "old-frame", "old-item" }.OrderBy(key => key),
            deleted.OrderBy(key => key));
    }

    [Fact]
    public void PlainLabel_TypeChange_SourceHandleStillOwnsSinglePrimary()
    {
        var afterTypeChange = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "K1",
            candidates:
            [
                Candidate("plain", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 0);
        var afterRefresh = TimberElementLabelMatchRules.SelectLabelForUpsert(
            sourceHandle: "H1",
            currentElementId: "KL1",
            previousElementId: "KL1",
            candidates:
            [
                Candidate("plain", "KL1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            currentElementOwnerCount: 1,
            previousElementOwnerCount: 1);

        Assert.Equal("plain", afterTypeChange.LabelKeyToUpdate);
        Assert.Equal("plain", afterRefresh.LabelKeyToUpdate);
        Assert.Empty(afterTypeChange.LabelKeysToDelete);
        Assert.Empty(afterRefresh.LabelKeysToDelete);
    }

    private static IReadOnlyList<string> OwnershipCleanup(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string preferredElementId) =>
        TimberMainAnnotationOwnershipRules
            .SelectSupersededLegacyFramedLeaderKeys(candidates)
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectExtraG4GroupKeysToDelete(
                    candidates,
                    preferredElementId))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
                    candidates,
                    preferredElementId))
            .Concat(
                TimberMainAnnotationOwnershipRules.SelectStaleElementIdKeysToDelete(
                    candidates,
                    preferredElementId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TimberElementLabelCandidate Candidate(
        string key,
        string elementId,
        string sourceHandle,
        TimberMainAnnotationComponentRole role,
        string? groupId = null) =>
        new()
        {
            LabelKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
            ComponentRole = role,
            AnnotationGroupId = groupId,
        };

    private static TimberFramedG4CompositeCandidate Leader(
        string key,
        string elementId,
        string sourceHandle,
        string groupId) =>
        CandidateG4(key, elementId, sourceHandle, groupId, TimberMainAnnotationComponentRole.CircleLeaderLine);

    private static TimberFramedG4CompositeCandidate Frame(
        string key,
        string elementId,
        string sourceHandle,
        string groupId) =>
        CandidateG4(key, elementId, sourceHandle, groupId, TimberMainAnnotationComponentRole.CircleFrame);

    private static TimberFramedG4CompositeCandidate Item(
        string key,
        string elementId,
        string sourceHandle,
        string groupId) =>
        CandidateG4(key, elementId, sourceHandle, groupId, TimberMainAnnotationComponentRole.CircleText);

    private static TimberFramedG4CompositeCandidate CandidateG4(
        string key,
        string elementId,
        string sourceHandle,
        string groupId,
        TimberMainAnnotationComponentRole role) =>
        new()
        {
            EntityKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
            ComponentRole = role,
            AnnotationGroupId = groupId,
        };
}
