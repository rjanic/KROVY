using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterGenerationRecipeRulesTests
{
    [Fact]
    public void UnanimousMembers_RecoverExactRecipe()
    {
        var members = new[]
        {
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
        };

        Assert.True(RoofRafterGenerationRecipeRules.TryUnify(members, out var recipe));
        Assert.Equal(80d, recipe.WidthMm);
        Assert.Equal(160d, recipe.HeightMm);
        Assert.Equal(900d, recipe.MaximumSpacingMm);
        Assert.Equal("Smrek C24", recipe.Material);
    }

    [Fact]
    public void LaterGlobalLikeDifferentWidth_DoesNotUnify()
    {
        var members = new[]
        {
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
            new RoofRafterGenerationRecipe(100d, 160d, 900d, "Smrek C24"),
        };

        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(members, out _));
    }

    [Theory]
    [InlineData(80d, 200d, 900d, "Smrek C24")]
    [InlineData(80d, 160d, 700d, "Smrek C24")]
    [InlineData(80d, 160d, 900d, "Dub")]
    public void DivergentHeightSpacingOrMaterial_DoesNotUnify(
        double width,
        double height,
        double spacing,
        string material)
    {
        var members = new[]
        {
            new RoofRafterGenerationRecipe(80d, 160d, 900d, "Smrek C24"),
            new RoofRafterGenerationRecipe(width, height, spacing, material),
        };

        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(members, out _));
    }

    [Fact]
    public void EmptyOrInvalid_DoesNotUnify()
    {
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify([], out _));
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [new RoofRafterGenerationRecipe(0d, 160d, 900d, "Smrek C24")],
            out _));
        Assert.False(RoofRafterGenerationRecipeRules.TryUnify(
            [new RoofRafterGenerationRecipe(80d, 160d, 900d, " ")],
            out _));
    }
}
