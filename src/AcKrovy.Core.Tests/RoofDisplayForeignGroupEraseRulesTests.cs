using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayForeignGroupEraseRulesTests
{
    [Fact]
    public void StrictForeignGroup_SelectsSevenStaleOwnerDisplayLines()
    {
        Assert.True(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                sourceHasValidRoofDefinition: true,
                BuildValidMembers(ownerHint: "291A"),
                out var keys));
        Assert.Equal(7, keys.Count);
        Assert.Equal(7, keys.Distinct().Count());
    }

    [Fact]
    public void StaleOwnerString_DoesNotPreventEraseSelection()
    {
        var members = BuildValidMembers(ownerHint: "STALE-OWNER");
        Assert.True(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out var keys));
        Assert.Equal(7, keys.Count);
    }

    [Fact]
    public void Stale1005Hint_DoesNotPreventEraseSelection()
    {
        // Observation model has no 1005 field by design: eligibility is structural.
        var members = BuildValidMembers(ownerHint: "1005-STALE");
        Assert.True(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void MissingRole_RejectsFallback()
    {
        var members = BuildValidMembers(ownerHint: "291A")
            .Where(m => m.Role != RoofDisplayEdgeRole.GableSlope11)
            .Append(new RoofDisplayForeignGroupMemberObservation(
                "dup-ridge",
                RoofDisplayForeignGroupMemberKind.DisplayLine,
                true,
                true,
                RoofDisplayEdgeRole.Ridge))
            .ToList();
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void DuplicateRole_RejectsFallback()
    {
        var members = BuildValidMembers(ownerHint: "291A").ToList();
        members[^1] = members[^1] with { Role = RoofDisplayEdgeRole.Ridge };
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void UnrelatedLine_RejectsFallback()
    {
        var members = BuildValidMembers(ownerHint: "291A").ToList();
        members[^1] = new RoofDisplayForeignGroupMemberObservation(
            "other",
            RoofDisplayForeignGroupMemberKind.Other,
            false,
            false,
            null);
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void SourceWithoutValidRoofDefinition_RejectsFallback()
    {
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                sourceHasValidRoofDefinition: false,
                BuildValidMembers(ownerHint: "291A"),
                out _));
    }

    [Fact]
    public void IncompleteMemberCount_RejectsFallback()
    {
        var members = BuildValidMembers(ownerHint: "291A").Take(7).ToList();
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void UnsupportedSchema_RejectsFallback()
    {
        var members = BuildValidMembers(ownerHint: "291A").ToList();
        members[^1] = members[^1] with { SchemaSupported = false };
        Assert.False(
            RoofDisplayForeignGroupEraseRules.TrySelectDisplayEraseMemberKeys(
                true,
                members,
                out _));
    }

    [Fact]
    public void UniqueCandidateSets_Resolve_AmbiguousDifferingSets_Reject()
    {
        Assert.True(
            RoofDisplayForeignGroupEraseRules.TryResolveUniqueEraseMemberKeys(
                [
                    ["A", "B", "C", "D", "E", "F", "G"],
                    ["G", "F", "E", "D", "C", "B", "A"],
                ],
                out var same));
        Assert.Equal(7, same.Count);

        Assert.False(
            RoofDisplayForeignGroupEraseRules.TryResolveUniqueEraseMemberKeys(
                [
                    ["A", "B", "C", "D", "E", "F", "G"],
                    ["A", "B", "C", "D", "E", "F", "X"],
                ],
                out _));
    }

    [Fact]
    public void UnionDeduplicatesAcrossInspectedOwnerMatchedAndForeign()
    {
        var union = RoofDisplayForeignGroupEraseRules.UnionDeduplicateEraseMemberKeys(
            ["1", "2"],
            ["2", "3"],
            ["3", "4", "5", "6", "7", "8", "1"]);
        Assert.Equal(8, union.Count);
        Assert.Equal(8, union.Distinct().Count());
    }

    private static IReadOnlyList<RoofDisplayForeignGroupMemberObservation> BuildValidMembers(
        string ownerHint)
    {
        _ = ownerHint; // Stale owner must not participate in eligibility.
        var roles = new[]
        {
            RoofDisplayEdgeRole.Ridge,
            RoofDisplayEdgeRole.Eave0,
            RoofDisplayEdgeRole.Eave1,
            RoofDisplayEdgeRole.GableSlope00,
            RoofDisplayEdgeRole.GableSlope01,
            RoofDisplayEdgeRole.GableSlope10,
            RoofDisplayEdgeRole.GableSlope11,
        };
        var members = new List<RoofDisplayForeignGroupMemberObservation>
        {
            new(
                "source",
                RoofDisplayForeignGroupMemberKind.SourcePolyline,
                false,
                false,
                null),
        };
        for (var i = 0; i < roles.Length; i++)
        {
            members.Add(new RoofDisplayForeignGroupMemberObservation(
                $"line-{i}",
                RoofDisplayForeignGroupMemberKind.DisplayLine,
                HasReadableRoofDisplayMetadata: true,
                SchemaSupported: true,
                Role: roles[i]));
        }

        return members;
    }
}
