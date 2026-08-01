using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationScaleOverridePreservationTests
{
    [Fact]
    public void ApplyCuttingAllowance_PreservesOverride()
    {
        var source = Source();
        var profile = TimberElementDefaultProfile.CreateDefault();

        var result = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            source,
            profile);

        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void ElementIdUpdate_PreservesOverride()
    {
        var result = Source() with { ElementId = "K12" };

        Assert.Equal("K12", result.ElementId);
        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void ApplyDefaultAnnotationMode_PreservesOverride()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.DefaultAnnotationMode = TimberAnnotationMode.NoAnnotations;
        profile.DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Rectangle;

        var result = TimberElementDefaultApplicator.ApplyAnnotationMode(
            Source(),
            profile);

        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void ApplyCustomDefinition_PreservesOverride()
    {
        var definition = new CustomElementDefinition(
            "custom-id",
            "Custom Beam",
            "CB");

        var result = CustomElementDefinitionRules.Apply(Source(), definition);

        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void RenameCustomDefinition_PreservesOverride()
    {
        var source = Source() with
        {
            ElementType = TimberElementType.Custom,
            CustomElementTypeId = "custom-id",
            CustomElementTypeName = "Old Beam",
            CustomElementTypePrefix = "CB",
        };
        var renamed = new CustomElementDefinition(
            "custom-id",
            "New Beam",
            "CB");

        var result = CustomElementDefinitionRenameRules.Apply(source, renamed);

        Assert.Equal("New Beam", result.CustomElementTypeName);
        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void NewTimberElement_UsesExplicitProfileScaleOverride()
    {
        Assert.Null(new TimberElementData().AnnotationScaleDenominatorOverride);
        Assert.Equal(50, TimberElementDefaults.For(TimberElementType.Rafter)
            .AnnotationScaleDenominatorOverride);

        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.AnnotationScaleDenominator = 25;

        Assert.Equal(25, TimberElementDefaults.For(TimberElementType.Rafter, profile)
            .AnnotationScaleDenominatorOverride);
    }

    private static TimberElementData Source() =>
        TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K7",
            AnnotationScaleDenominatorOverride = 25,
        };
}
