using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinElementIdAllocationRulesTests
{
    [Fact]
    public void SecondRun_UnchangedMembers_PreserveEveryElementId()
    {
        var owners = Owners(
            ("V1", "2937"),
            ("V2", "293B"),
            ("V3", "293C"),
            ("V4", "293D"),
            ("V5", "293E"));
        var requests = new[]
        {
            Req("2937", "V1"),
            Req("293B", "V2"),
            Req("293C", "V3"),
            Req("293D", "V4"),
            Req("293E", "V5"),
        };

        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            requests,
            owners);

        Assert.Equal(new[] { "V1", "V2", "V3", "V4", "V5" }, assigned);
    }

    [Fact]
    public void SelfExclusion_SameEntityAloneDoesNotForceRenumber()
    {
        var owners = Owners(("V4", "self"));
        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            new[] { Req("self", "V4") },
            owners);

        Assert.Equal(new[] { "V4" }, assigned);
    }

    [Fact]
    public void OtherOwnerCollision_ReplacesOnlyConflictingIds()
    {
        var owners = Owners(
            ("V1", "ridge"),
            ("V2", "manual-a"),
            ("V2", "inter-1"),
            ("V3", "manual-b"),
            ("V3", "inter-2"),
            ("V4", "inter-3"),
            ("V5", "inter-4"));
        var requests = new[]
        {
            Req("ridge", "V1"),
            Req("inter-1", "V2"),
            Req("inter-2", "V3"),
            Req("inter-3", "V4"),
            Req("inter-4", "V5"),
        };

        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            requests,
            owners);

        Assert.Equal("V1", assigned[0]);
        Assert.Equal("V4", assigned[3]);
        Assert.Equal("V5", assigned[4]);
        Assert.Equal(new[] { "V6", "V7" }, assigned.Skip(1).Take(2).OrderBy(id => id).ToArray());
        Assert.Equal(5, assigned.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("V2", assigned);
        Assert.DoesNotContain("V3", assigned);
    }

    [Fact]
    public void FirstRun_SkipsExistingManualVIds_AndKeepsThemUntouched()
    {
        var owners = Owners(
            ("V1", "manual-1"),
            ("V3", "manual-3"),
            ("V4", "rafter-with-v4"));
        var requests = new[]
        {
            Req(null, string.Empty),
            Req(null, string.Empty),
            Req(null, string.Empty),
        };

        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            requests,
            owners);

        Assert.Equal(new[] { "V2", "V5", "V6" }, assigned);
        Assert.Contains("V1", owners.Keys);
        Assert.Contains("V3", owners.Keys);
        Assert.Contains("V4", owners.Keys);
    }

    [Fact]
    public void BatchReservation_PreventsTransactionLocalCollisionsAmongNewMembers()
    {
        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            new[]
            {
                Req(null, string.Empty),
                Req(null, string.Empty),
                Req(null, string.Empty),
            },
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[] { "V1", "V2", "V3" }, assigned);
        Assert.Equal(3, assigned.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TwoPhase_DoesNotStealPreservableSiblingIdsWhileReplacingCollisions()
    {
        // Old count-based sequential allocator could steal V4/V5 before siblings preserved
        // them when earlier members failed uniqueness. Two-phase must preserve first.
        var owners = Owners(
            ("V1", "ridge"),
            ("V2", "foreign"),
            ("V2", "a"),
            ("V3", "foreign"),
            ("V3", "b"),
            ("V4", "c"),
            ("V5", "d"));
        var requests = new[]
        {
            Req("ridge", "V1"),
            Req("a", "V2"),
            Req("b", "V3"),
            Req("c", "V4"),
            Req("d", "V5"),
        };

        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            requests,
            owners);

        Assert.Equal(new[] { "V1", "V6", "V7", "V4", "V5" }, assigned);
    }

    [Fact]
    public void AbsentFromScan_StillPreservesWhenNoOtherOwnerExists()
    {
        var owners = Owners(("V1", "ridge"));
        var requests = new[]
        {
            Req("ridge", "V1"),
            Req("hidden-inter", "V2"),
        };

        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            requests,
            owners);

        Assert.Equal(new[] { "V1", "V2" }, assigned);
    }

    [Fact]
    public void FalseCountOne_DoesNotPreserveForeignOwnedId()
    {
        // count==1 for V2 owned only by foreign timber must NOT preserve for self.
        var owners = Owners(("V2", "foreign"));
        var assigned = RoofAutomaticPurlinElementIdAllocationRules.Assign(
            TimberElementType.Purlin,
            new[] { Req("self", "V2") },
            owners);

        Assert.Equal(new[] { "V1" }, assigned);
    }

    [Fact]
    public void CreateTimberData_IsIdempotentWithPersistedPrepareForWriteShape()
    {
        var first = RoofAutomaticPurlinMaterializationRules.CreateTimberData(
            TimberElementDefaultProfile.CreateDefault(),
            "V2");
        var second = RoofAutomaticPurlinMaterializationRules.CreateTimberData(
            TimberElementDefaultProfile.CreateDefault(),
            "V2");
        var rewritten = TimberElementDataVersioning.PrepareForWrite(first);

        Assert.Equal(first, second);
        Assert.Equal(first, rewritten);
        Assert.Equal(TimberAnnotationMode.NoAnnotations, first.AnnotationMode);
        Assert.Equal(LengthCalculationMode.PlanLength, first.LengthCalculationMode);
    }

    private static RoofAutomaticPurlinElementIdRequest Req(
        string? token,
        string currentElementId) => new(token, currentElementId);

    private static Dictionary<string, IReadOnlyCollection<string>> Owners(
        params (string ElementId, string Owner)[] pairs)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (elementId, owner) in pairs)
        {
            if (!map.TryGetValue(elementId, out var list))
            {
                list = new List<string>();
                map[elementId] = list;
            }

            list.Add(owner);
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
