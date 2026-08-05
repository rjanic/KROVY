using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedBlockContentPolicyTests
{
    [Fact]
    public void CanonicalName_UsesAkKrovyFbcR2FamilyAndIsSafe()
    {
        var raw = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Circle,
            "SMALL",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        var name = AutoCadFramedBlockContentPolicy.CreateCanonicalName(raw);

        Assert.StartsWith("AK_KROVY_FBC_R2_", name, StringComparison.Ordinal);
        Assert.Contains(
            TimberFramedBlockContentVariantRules.DimensionsNegativeXToken,
            name,
            StringComparison.Ordinal);
        Assert.True(AutoCadFramedBlockContentPolicy.IsSafeSymbolName(name));
        Assert.True(AutoCadFramedBlockContentPolicy.IsProductionFamilyName(name));
        Assert.DoesNotContain("AK_G5C_", name, StringComparison.Ordinal);
        Assert.DoesNotContain("AK_DEV_", name, StringComparison.Ordinal);
        Assert.DoesNotContain("25", name, StringComparison.Ordinal);
        Assert.DoesNotContain("100", name, StringComparison.Ordinal);
        Assert.Equal(name, AutoCadFramedBlockContentPolicy.CreateCanonicalName(raw));
    }

    [Fact]
    public void CollisionName_IsDeterministicDistinctAndSafe()
    {
        var raw = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Slot,
            "MEDIUM",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.ItemOnly);
        var canonical = AutoCadFramedBlockContentPolicy.CreateCanonicalName(raw);
        var first = AutoCadFramedBlockContentPolicy.CreateCollisionName(raw, 1);
        var second = AutoCadFramedBlockContentPolicy.CreateCollisionName(raw, 1);
        var other = AutoCadFramedBlockContentPolicy.CreateCollisionName(raw, 2);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.NotEqual(canonical, first);
        Assert.Contains("_C", first, StringComparison.Ordinal);
        Assert.True(AutoCadFramedBlockContentPolicy.IsSafeSymbolName(first));
    }

    [Fact]
    public void Select_ReusesMatchingAndCreatesOnMissing()
    {
        var raw = TimberFramedBlockContentVariantRules.CreateRawKey(
            TimberFramedBlockContentKind.Plain,
            "NONE",
            "Standard",
            "Standard",
            2.7d,
            2.5d,
            TimberFramedBlockContentPresentation.Combined,
            TimberFramedBlockContentDimensionColumnSide.PositiveLocalX);
        var canonical = AutoCadFramedBlockContentPolicy.CreateCanonicalName(raw);

        var create = AutoCadFramedBlockContentPolicy.Select(
            raw,
            _ => AutoCadFramedBlockContentCandidateState.Missing);
        Assert.Equal(
            AutoCadFramedBlockContentCollisionDecisionKind.Create,
            create.Kind);
        Assert.Equal(canonical, create.CandidateName);
        Assert.False(create.IsCollision);

        var reuse = AutoCadFramedBlockContentPolicy.Select(
            raw,
            name => name == canonical
                ? AutoCadFramedBlockContentCandidateState.Matching
                : AutoCadFramedBlockContentCandidateState.Missing);
        Assert.Equal(
            AutoCadFramedBlockContentCollisionDecisionKind.Reuse,
            reuse.Kind);
        Assert.False(reuse.IsCollision);

        var collision = AutoCadFramedBlockContentPolicy.Select(
            raw,
            name => name == canonical
                ? AutoCadFramedBlockContentCandidateState.Invalid
                : AutoCadFramedBlockContentCandidateState.Missing);
        Assert.Equal(
            AutoCadFramedBlockContentCollisionDecisionKind.Create,
            collision.Kind);
        Assert.True(collision.IsCollision);
        Assert.NotEqual(canonical, collision.CandidateName);
    }

    [Theory]
    [InlineData(TimberFramedBlockContentKind.Plain, TimberFramedBlockContentPresentation.Combined, 1, 3)]
    [InlineData(TimberFramedBlockContentKind.Circle, TimberFramedBlockContentPresentation.Combined, 1, 3)]
    [InlineData(TimberFramedBlockContentKind.Rectangle, TimberFramedBlockContentPresentation.Combined, 1, 3)]
    [InlineData(TimberFramedBlockContentKind.Slot, TimberFramedBlockContentPresentation.Combined, 1, 3)]
    [InlineData(TimberFramedBlockContentKind.Circle, TimberFramedBlockContentPresentation.ItemOnly, 1, 1)]
    [InlineData(TimberFramedBlockContentKind.Rectangle, TimberFramedBlockContentPresentation.ItemOnly, 1, 1)]
    [InlineData(TimberFramedBlockContentKind.Slot, TimberFramedBlockContentPresentation.ItemOnly, 1, 1)]
    public void FamilyInventory_MatchesContract(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentPresentation presentation,
        int frames,
        int attrs)
    {
        Assert.Equal(
            frames,
            TimberFramedBlockContentDefinitionRules.ExpectedFrameEntityCount(kind));
        Assert.Equal(
            attrs,
            TimberFramedBlockContentDefinitionRules.ExpectedAttributeCount(presentation));
    }
}
