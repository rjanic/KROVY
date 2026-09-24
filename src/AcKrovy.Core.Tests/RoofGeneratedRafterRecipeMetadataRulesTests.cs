using AcKrovy.Core.Models;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedRafterRecipeMetadataRulesTests
{
    [Fact]
    public void MutatesRecipeDefiningFields_DetectsWidthHeightMaterialAndType()
    {
        var original = Sample();
        Assert.True(RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            original with { WidthMm = 120d }));
        Assert.True(RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            original with { HeightMm = 180d }));
        Assert.True(RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            original with { Material = "Dub" }));
        Assert.True(RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            original with { ElementType = TimberElementType.Purlin }));
    }

    [Fact]
    public void MutatesRecipeDefiningFields_IgnoresAnnotationAndNoteChanges()
    {
        var original = Sample();
        Assert.False(RoofGeneratedRafterRecipeMetadataRules.MutatesRecipeDefiningFields(
            original,
            original with
            {
                Note = "manual note",
                AnnotationMode = TimberAnnotationMode.NoAnnotations,
                CuttingAllowanceMm = 40d,
            }));
    }

    private static TimberElementData Sample() =>
        new()
        {
            ElementId = "K1",
            ElementType = TimberElementType.Rafter,
            WidthMm = 100d,
            HeightMm = 100d,
            Material = "Smrek C24",
        };
}
