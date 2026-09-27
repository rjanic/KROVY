using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofNativeCloneOwnershipRulesTests
{
    private static readonly string[] Children = Enumerable.Range(0, 5).Select(i => $"plan{i}").ToArray();
    private static readonly string[] Existing = Children.Append("2912").ToArray();
    private static Dictionary<string, string> Mapping(string suffix = "") =>
        Existing.ToDictionary(id => id, id => id == "2912" ? "294D" + suffix : id + "clone" + suffix);

    [Theory]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    public void FullNativeMap_IndependentOfInheritedOwnerAndGeometry(string command)
    {
        Assert.NotEmpty(command); // Both commands use the same provenance policy.
        Assert.True(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, Mapping(), Existing));
    }

    [Fact]
    public void SourceOnly_IsPartial()
    {
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Children,
            new Dictionary<string, string> { ["2912"] = "294D" }, Existing));
    }

    [Theory]
    [InlineData("2912")]
    [InlineData("plan0")]
    [InlineData("plan4")]
    public void MissingRequiredNativeMember_IsPartial(string missing)
    {
        var map = Mapping();
        map.Remove(missing);
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, map, Existing));
    }

    [Fact]
    public void MappingToOriginal_IsRejected()
    {
        var map = Mapping();
        map["plan0"] = "plan4";
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, map, Existing));
    }

    [Fact]
    public void AliasedDestinations_AreRejected()
    {
        var map = Mapping();
        map["plan0"] = map["plan1"];
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, map, Existing));
    }

    [Fact]
    public void RepeatedCopies_KeepSeparateNativeBatches()
    {
        var first = Mapping("A");
        var second = Mapping("B");
        Assert.True(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, first, Existing));
        Assert.True(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, second, Existing));
        Assert.Empty(first.Values.Intersect(second.Values));
    }

    [Fact]
    public void MemberOnlyWithoutMappedOwner_IsPartial()
    {
        var map = Mapping();
        map.Remove("2912");
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Children, map, Existing));
    }

    [Fact]
    public void EmptyAssembly_IsNotWholeRoof()
    {
        Assert.False(RoofNativeCloneOwnershipRules.IsComplete("2912", Array.Empty<string>(), Mapping(), Existing));
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    public void FullPhysicalRoof_DisposesExactly13PhysicalAnd5PlanClones(string command)
    {
        var physical = Enumerable.Range(0, 13).Select(i => "physical" + i).ToArray();
        var disposable = Children.Concat(physical).ToArray();
        var original = Existing.Concat(physical).Append("unrelated-owner").ToArray();
        var map = disposable.Append("2912").ToDictionary(id => id, id => command + "-" + id);
        Assert.True(RoofNativeCloneOwnershipRules.TryGetDisposableClones("2912", Children,
            disposable, map, original, out var erase));
        Assert.Equal(18, erase.Count);
        Assert.Empty(erase.Intersect(original));
        Assert.DoesNotContain(map["2912"], erase);
        Assert.Equal(20, original.Length); // Original snapshot is unchanged.
    }

    [Fact]
    public void PhysicalMappingToOriginalOrOwner_RollsBackEntirePlan()
    {
        var original = Existing.Append("physical").ToArray();
        var disposable = Children.Append("physical").ToArray();
        var map = Mapping();
        map["physical"] = "physical";
        Assert.False(RoofNativeCloneOwnershipRules.TryGetDisposableClones("2912", Children,
            disposable, map, original, out var erase));
        Assert.Empty(erase);
        map["physical"] = map["2912"];
        Assert.False(RoofNativeCloneOwnershipRules.TryGetDisposableClones("2912", Children,
            disposable, map, original, out erase));
        Assert.Empty(erase);
    }

    [Fact]
    public void GroupCopyWithoutPhysicalChildren_OnlyDisposesMappedPlan()
    {
        Assert.True(RoofNativeCloneOwnershipRules.TryGetDisposableClones("2912", Children,
            Children.Append("physical").ToArray(), Mapping(), Existing.Append("physical").ToArray(), out var erase));
        Assert.Equal(5, erase.Count);
    }
}
