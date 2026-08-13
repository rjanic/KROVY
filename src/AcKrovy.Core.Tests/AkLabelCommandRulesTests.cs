using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AkLabelCommandRulesTests
{
    [Fact]
    public void MissingOnly_ExistingAnnotation_IsNoOp_EvenWhenManuallyRotatedConceptually()
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.MissingOnly,
            TimberAnnotationMode.DimensionsWithItemNumber,
            hasExistingMainAnnotation: true);

        Assert.Equal(AkLabelSourceAction.NoOp, action);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber)]
    public void MissingOnly_MissingAnnotation_Ensures(TimberAnnotationMode mode)
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.MissingOnly,
            mode,
            hasExistingMainAnnotation: false);

        Assert.Equal(AkLabelSourceAction.EnsureMissing, action);
    }

    [Fact]
    public void MissingOnly_FramedAndR3Existing_Untouched()
    {
        Assert.Equal(
            AkLabelSourceAction.NoOp,
            AkLabelCommandRules.Decide(
                AkLabelIntention.MissingOnly,
                TimberAnnotationMode.ItemNumberLeader,
                hasExistingMainAnnotation: true));
        Assert.Equal(
            AkLabelSourceAction.NoOp,
            AkLabelCommandRules.Decide(
                AkLabelIntention.MissingOnly,
                TimberAnnotationMode.DimensionsWithItemNumber,
                hasExistingMainAnnotation: true));
    }

    [Fact]
    public void ResetSelected_ForcesCanonicalRecreate()
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.ResetSelected,
            TimberAnnotationMode.FullLabel,
            hasExistingMainAnnotation: true);

        Assert.Equal(AkLabelSourceAction.ForceCanonicalRecreate, action);
    }

    [Fact]
    public void ResetAll_ForcesCanonicalRecreate()
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.ResetAll,
            TimberAnnotationMode.DimensionsWithItemNumber,
            hasExistingMainAnnotation: true);

        Assert.Equal(AkLabelSourceAction.ForceCanonicalRecreate, action);
    }

    [Fact]
    public void Reset_WhenNoAnnotationsAndNothingPresent_IsNoOp()
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.ResetAll,
            TimberAnnotationMode.NoAnnotations,
            hasExistingMainAnnotation: false);

        Assert.Equal(AkLabelSourceAction.NoOp, action);
    }

    [Fact]
    public void MissingOnly_NoAnnotationsWithOrphan_EnsuresCleanup()
    {
        var action = AkLabelCommandRules.Decide(
            AkLabelIntention.MissingOnly,
            TimberAnnotationMode.NoAnnotations,
            hasExistingMainAnnotation: true);

        Assert.Equal(AkLabelSourceAction.EnsureMissing, action);
    }

    [Fact]
    public void HasExistingMainAnnotationForSource_IsCaseInsensitive()
    {
        Assert.True(AkLabelCommandRules.HasExistingMainAnnotationForSource(
            "ABC",
            new[] { "abc", "zzz" }));
        Assert.False(AkLabelCommandRules.HasExistingMainAnnotationForSource(
            "ABC",
            new[] { "zzz" }));
    }
}
