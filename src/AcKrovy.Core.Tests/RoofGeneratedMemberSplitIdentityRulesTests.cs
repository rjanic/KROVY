using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberSplitIdentityRulesTests
{
    [Fact]
    public void Break_UsesSnapshotHandle_AsGenerated()
    {
        var live = new[] { "29AD", "29B0" };
        var snapshot = new[] { "29AD" };
        var appended = new[] { "29B0" };

        Assert.True(RoofGeneratedMemberSplitIdentityRules.TryResolveFragments(
            live,
            snapshot,
            appended,
            out var generated,
            out var standalone));
        Assert.Equal("29AD", generated);
        Assert.Equal(["29B0"], standalone);
    }

    [Fact]
    public void WhenSnapshotMissing_UsesNonAppended_AsGenerated()
    {
        var live = new[] { "29AD", "29B0" };
        var snapshot = Array.Empty<string>();
        var appended = new[] { "29B0" };

        Assert.True(RoofGeneratedMemberSplitIdentityRules.TryResolveFragments(
            live,
            snapshot,
            appended,
            out var generated,
            out var standalone));
        Assert.Equal("29AD", generated);
        Assert.Equal(["29B0"], standalone);
    }

    [Fact]
    public void AmbiguousSnapshotMatches_Fail()
    {
        var live = new[] { "29AD", "29AE" };
        var snapshot = new[] { "29AD", "29AE" };

        Assert.False(RoofGeneratedMemberSplitIdentityRules.TryResolveFragments(
            live,
            snapshot,
            appendedHandles: [],
            out _,
            out _));
    }
}
