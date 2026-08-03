using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadFramedItemLeaderRendererPolicyTests
{
    [Theory]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Circle,
        TimberMainAnnotationComponentRole.Primary, true)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Slot,
        TimberMainAnnotationComponentRole.FramedItem, true)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain,
        TimberMainAnnotationComponentRole.Primary, false)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Plain,
        TimberMainAnnotationComponentRole.FramedItem, false)]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Rectangle,
        TimberMainAnnotationComponentRole.Primary, false)]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Circle,
        TimberMainAnnotationComponentRole.Primary, false)]
    [InlineData(TimberAnnotationMode.NoAnnotations, ItemNumberLeaderStyle.Circle,
        TimberMainAnnotationComponentRole.Primary, false)]
    public void Scope_UsesVariantsOnlyForTheFramedItemNumberComponent(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationComponentRole role,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutoCadFramedItemLeaderRendererPolicy.UsesImmutableVariant(
                mode,
                style,
                role));
    }

    [Theory]
    [InlineData(25, 0.5d)]
    [InlineData(50, 1d)]
    [InlineData(75, 1.5d)]
    [InlineData(100, 2d)]
    [InlineData(250, 5d)]
    public void BlockScale_UsesTheCentralDenominatorOver50Authority(
        int denominator,
        double expected)
    {
        var context = new TimberAnnotationScaleContext(
            denominator,
            TimberAnnotationScaleSource.ElementOverride);

        Assert.Equal(
            expected,
            AutoCadFramedItemLeaderRendererPolicy.CalculateBlockScale(context));
        Assert.Equal(
            denominator / (double)TimberAnnotationScaleRules.DefaultDenominator,
            context.ScaleFactor);
    }

    [Fact]
    public void MutationPolicy_DoesNotOpenOrChangeAnExistingLeaderAfterEnsureFailure()
    {
        var plan = AutoCadFramedItemLeaderMutationPolicy.Create(
            variantEnsureSucceeded: false,
            hasExistingAnnotation: true,
            blockContentMatches: false,
            blockScaleMatches: false,
            itemNumberTokenMatches: false);

        Assert.False(plan.ShouldOpenExistingForWrite);
        Assert.False(plan.ShouldReplaceBlockContent);
        Assert.False(plan.ShouldSetBlockScale);
        Assert.False(plan.ShouldSetItemNumberToken);
        Assert.True(plan.PreserveExistingAnnotation);
    }

    [Fact]
    public void MutationPolicy_ReappliesTokenAfterLegacyBlockContentMigration()
    {
        var plan = AutoCadFramedItemLeaderMutationPolicy.Create(
            variantEnsureSucceeded: true,
            hasExistingAnnotation: true,
            blockContentMatches: false,
            blockScaleMatches: true,
            itemNumberTokenMatches: true);

        Assert.True(plan.ShouldOpenExistingForWrite);
        Assert.True(plan.ShouldReplaceBlockContent);
        Assert.False(plan.ShouldSetBlockScale);
        Assert.True(plan.ShouldSetItemNumberToken);
        Assert.False(plan.PreserveExistingAnnotation);
    }

    [Fact]
    public void MutationPolicy_IsNoOpForMatchingVariantScaleAndToken()
    {
        var plan = AutoCadFramedItemLeaderMutationPolicy.Create(
            variantEnsureSucceeded: true,
            hasExistingAnnotation: true,
            blockContentMatches: true,
            blockScaleMatches: true,
            itemNumberTokenMatches: true);

        Assert.False(plan.ShouldOpenExistingForWrite);
        Assert.False(plan.ShouldReplaceBlockContent);
        Assert.False(plan.ShouldSetBlockScale);
        Assert.False(plan.ShouldSetItemNumberToken);
    }

    [Fact]
    public void FailedKeyLookup_DoesNotPoisonBatchForAnotherValidKey()
    {
        var identity = new AutoCadDatabaseIdentityToken(0x1234);
        var index = new AutoCadItemLeaderBlockVariantBatchIndex<string>(identity);
        var failedKey = AutoCadItemLeaderBlockVariantKey.Create(
            AutoCadItemLeaderBlockFrameKind.Circle,
            TimberItemLeaderBlockSize.Small);
        var validKey = AutoCadItemLeaderBlockVariantKey.Create(
            AutoCadItemLeaderBlockFrameKind.Slot,
            TimberItemLeaderBlockSize.Small);

        Assert.False(index.TryGet(identity, failedKey, out _));
        Assert.Equal(0, index.Count);
        index.Add(identity, validKey, "valid-id", "VALID_BLOCK", false);

        Assert.False(index.TryGet(identity, failedKey, out _));
        Assert.True(index.TryGet(identity, validKey, out var valid));
        Assert.Equal("valid-id", valid!.DefinitionId);
        Assert.Equal(1, index.Count);
    }
}
