using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Portable A–H contract for framed Combined G5 production ownership.
/// Host geometry is verified separately; these rules guarantee entity-count
/// and migration cleanup semantics used by ElementLabelService.
/// </summary>
public sealed class TimberFramedCombinedG5ProductionOwnershipRulesTests
{
    [Fact]
    public void A_Create_RequiredRolesAreSingleFramedItem()
    {
        var required = TimberCompositeAnnotationLifecycleRules.RequiredRoles(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle);

        Assert.Equal(
            new[] { TimberMainAnnotationComponentRole.FramedItem },
            required);
    }

    [Fact]
    public void B_Refresh_SurplusRoleKeepsOneFramedItem()
    {
        var deleted = TimberMainAnnotationOwnershipRules.SelectSurplusRoleKeysToDelete(
            [
                G5("g5-a", "K1", "H1"),
                G5("g5-b", "K1", "H1"),
            ],
            preferredElementId: "K1");

        Assert.Single(deleted);
        Assert.Contains(deleted, key => key is "g5-a" or "g5-b");
    }

    [Fact]
    public void C_RefreshTwice_IdempotentWhenSingleG5Present()
    {
        var candidates = new[] { G5("g5", "K1", "H1") };
        var first = FullCleanup(candidates, "K1");
        var second = FullCleanup(candidates, "K1");

        Assert.Empty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void D_TypeChange_KrokvaToKliestina_KeepsOneG5DropsStale()
    {
        var deleted = FullCleanup(
            [
                G5("g5-new", "KL1", "H1"),
                G5("g5-old", "K1", "H1"),
                Candidate("primary-old", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "KL1");

        Assert.Contains("g5-old", deleted);
        Assert.Contains("primary-old", deleted);
        Assert.DoesNotContain("g5-new", deleted);
    }

    [Fact]
    public void E_TypeChange_KrokvaToVaznyTram_KeepsOneG5DropsStale()
    {
        var deleted = FullCleanup(
            [
                G5("g5-new", "VT1", "H1"),
                Candidate(
                    "leader",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleLeaderLine,
                    "g4"),
                Candidate(
                    "frame",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleFrame,
                    "g4"),
                Candidate(
                    "item",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleText,
                    "g4"),
                Candidate("primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ],
            preferredElementId: "VT1");

        Assert.DoesNotContain("g5-new", deleted);
        Assert.Contains("leader", deleted);
        Assert.Contains("frame", deleted);
        Assert.Contains("item", deleted);
        Assert.Contains("primary", deleted);
    }

    [Fact]
    public void F_OldG4Composite_MigratesAwayWhenG5Present()
    {
        var deleted =
            TimberMainAnnotationOwnershipRules.SelectLegacyCombinedPartsToDeleteWhenG5Present(
            [
                G5("g5", "K1", "H1"),
                Candidate(
                    "leader",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleLeaderLine,
                    "g4"),
                Candidate(
                    "frame",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleFrame,
                    "g4"),
                Candidate(
                    "item",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleText,
                    "g4"),
                Candidate("primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
            ]);

        Assert.Equal(
            new[] { "leader", "frame", "item", "primary" }.OrderBy(key => key),
            deleted.OrderBy(key => key));
        Assert.DoesNotContain("g5", deleted);
    }

    [Fact]
    public void F2_UnexpectedComponents_AfterG5RequiredRolesDropLegacyParts()
    {
        var unexpected =
            TimberCompositeAnnotationLifecycleRules.SelectUnexpectedComponentKeys(
                TimberAnnotationMode.DimensionsWithItemNumber,
                ItemNumberLeaderStyle.Rectangle,
                [
                    G5("g5", "K1", "H1"),
                    Candidate(
                        "leader",
                        "K1",
                        "H1",
                        TimberMainAnnotationComponentRole.CircleLeaderLine),
                    Candidate("primary", "K1", "H1", TimberMainAnnotationComponentRole.Primary),
                ]);

        Assert.Equal(
            new[] { "leader", "primary" }.OrderBy(key => key),
            unexpected.OrderBy(key => key));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d * Math.PI / 180d)]
    [InlineData(-35d * Math.PI / 180d)]
    [InlineData(Math.PI / 2d)]
    public void G_RotationContract_ReadableNormalizationPreserved(double axisRadians)
    {
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(axisRadians);
        Assert.InRange(readable, -Math.PI / 2d - 1e-9d, Math.PI / 2d + 1e-9d);

        var kind = TimberFramedBlockContentDefinitionRules.FromItemNumberLeaderStyle(
            ItemNumberLeaderStyle.Circle);
        Assert.Equal(TimberFramedBlockContentKind.Circle, kind);
    }

    [Fact]
    public void H_ForeignMLeader_IgnoredWithoutMatchingSourceHandle()
    {
        var deleted = FullCleanup(
            [
                G5("owned", "K1", "H1"),
                // Foreign annotation on another handle — no G5 ownership on that
                // handle, so Combined migration must not touch it.
                Candidate(
                    "foreign-mleader",
                    "K9",
                    "FOREIGN",
                    TimberMainAnnotationComponentRole.Primary),
                Candidate(
                    "foreign-g4",
                    "K9",
                    "FOREIGN",
                    TimberMainAnnotationComponentRole.CircleText,
                    "g4"),
            ],
            preferredElementId: "K1");

        Assert.Empty(deleted);
    }

    [Fact]
    public void G5FramedItem_NotSupersededByLeftoverG4CircleParts()
    {
        var deleted =
            TimberMainAnnotationOwnershipRules.SelectSupersededLegacyFramedLeaderKeys(
            [
                G5("g5", "K1", "H1"),
                Candidate(
                    "item",
                    "K1",
                    "H1",
                    TimberMainAnnotationComponentRole.CircleText,
                    "g4"),
            ]);

        Assert.Empty(deleted);
    }

    [Fact]
    public void CombinedPlain_StillRequiresPrimaryAndFramedItem()
    {
        var required = TimberCompositeAnnotationLifecycleRules.RequiredRoles(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Plain);

        Assert.Equal(
            new[]
            {
                TimberMainAnnotationComponentRole.Primary,
                TimberMainAnnotationComponentRole.FramedItem,
            }.OrderBy(role => role),
            required.OrderBy(role => role));
    }

    [Fact]
    public void ItemNumberLeaderFramed_StillRequiresG4StandaloneRoles()
    {
        var required = TimberCompositeAnnotationLifecycleRules.RequiredRoles(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Slot);

        Assert.Equal(
            new[]
            {
                TimberMainAnnotationComponentRole.CircleLeaderLine,
                TimberMainAnnotationComponentRole.CircleFrame,
                TimberMainAnnotationComponentRole.CircleText,
            }.OrderBy(role => role),
            required.OrderBy(role => role));
    }

    private static IReadOnlyList<string> FullCleanup(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string preferredElementId) =>
        TimberMainAnnotationOwnershipRules
            .SelectSupersededLegacyFramedLeaderKeys(candidates)
            .Concat(
                TimberMainAnnotationOwnershipRules
                    .SelectLegacyCombinedPartsToDeleteWhenG5Present(candidates))
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

    private static TimberElementLabelCandidate G5(
        string key,
        string elementId,
        string sourceHandle) =>
        new()
        {
            LabelKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
            ComponentRole = TimberMainAnnotationComponentRole.FramedItem,
            RendererGeneration = TimberMainAnnotationOwnershipRules.G5RendererGeneration,
        };

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
}
