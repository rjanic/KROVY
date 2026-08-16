using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedRafterCopyAssociationRulesTests
{
    private static readonly RoofRafterGenerationRecipe RecipeA =
        new(80d, 160d, 900d, "Smrek C24");

    [Fact]
    public void StaleSharedOwner_IsPartitionedByGeometryAcrossOriginalAndCopy()
    {
        var original = Geometry(0, 0);
        var copied = Geometry(20000, 0);
        var originalLayout = Layout(original, RecipeA);
        var copiedLayout = Layout(copied, RecipeA);
        var observations = Observations("291A", originalLayout, RecipeA)
            .Concat(Observations("291A", copiedLayout, RecipeA, keyPrefix: "copy-"))
            .ToArray();

        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(
            [
                new RoofGeneratedRafterCopyOwnerTarget("291A", original),
                new RoofGeneratedRafterCopyOwnerTarget("2996", copied),
            ],
            observations);

        Assert.Equal(2, plan.Associations.Count);
        var originalAssoc = Assert.Single(plan.Associations, item => item.OwnerReference == "291A");
        var copiedAssoc = Assert.Single(plan.Associations, item => item.OwnerReference == "2996");
        Assert.All(originalAssoc.Members, member => Assert.StartsWith("291A-", member.MemberKey));
        Assert.All(copiedAssoc.Members, member => Assert.StartsWith("copy-", member.MemberKey));
        Assert.False(originalAssoc.RequiresMetadataRewrite);
        Assert.True(copiedAssoc.RequiresMetadataRewrite);
        Assert.Equal(copiedLayout.Signature, copiedAssoc.ExpectedLayout.Signature);
    }

    [Fact]
    public void CompleteSetRequired_MissingStationRejects()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var observations = Observations("A", layout, RecipeA).Skip(1).ToArray();

        Assert.False(RoofGeneratedRafterCopyAssociationRules.TryMatchCompleteSet(
            layout,
            observations,
            out _));
    }

    [Fact]
    public void DuplicateStationRejectsAdoption()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var observations = Observations("A", layout, RecipeA).ToList();
        var first = observations[0];
        observations.Add(first with { MemberKey = "dup-" + first.MemberKey });

        Assert.False(RoofGeneratedRafterCopyAssociationRules.TryMatchCompleteSet(
            layout,
            observations,
            out _));
    }

    [Fact]
    public void WrongFaceRejectsAdoption()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var observations = Observations("A", layout, RecipeA)
            .Select(item => item.Face == RafterRoofFace.Face0
                ? item with { Face = RafterRoofFace.Face1 }
                : item)
            .ToArray();

        Assert.False(RoofGeneratedRafterCopyAssociationRules.TryMatchCompleteSet(
            layout,
            observations,
            out _));
    }

    [Fact]
    public void LineGeometryMismatchRejectsAdoption()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var observations = Observations("A", layout, RecipeA)
            .Select((item, index) => index == 0
                ? item with
                {
                    PlanStart = new RoofPoint2D(item.PlanStart.X + 50d, item.PlanStart.Y),
                }
                : item)
            .ToArray();

        Assert.False(RoofGeneratedRafterCopyAssociationRules.TryMatchCompleteSet(
            layout,
            observations,
            out _));
    }

    [Fact]
    public void AmbiguousMatchAcrossTwoRoofsRejectsBoth()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var observations = Observations("A", layout, RecipeA).ToArray();

        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(
            [
                new RoofGeneratedRafterCopyOwnerTarget("ROOF1", geometry),
                new RoofGeneratedRafterCopyOwnerTarget("ROOF2", geometry),
            ],
            observations);

        Assert.Empty(plan.Associations);
    }

    [Fact]
    public void UnrelatedRecipeRoofIsIgnored()
    {
        var roofA = Geometry(0, 0);
        var roofB = Geometry(30000, 0);
        var layoutA = Layout(roofA, RecipeA);
        var observations = Observations("A", layoutA, RecipeA).ToArray();

        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(
            [
                new RoofGeneratedRafterCopyOwnerTarget("A", roofA),
                new RoofGeneratedRafterCopyOwnerTarget("B", roofB),
            ],
            observations);

        Assert.Single(plan.Associations);
        Assert.Equal("A", plan.Associations[0].OwnerReference);
    }

    [Fact]
    public void MultipleCopies_A_APrime_ADoublePrime_Partition()
    {
        var a = Geometry(0, 0);
        var a1 = Geometry(15000, 0);
        var a2 = Geometry(30000, 0);
        var observations = Observations("A", Layout(a, RecipeA), RecipeA)
            .Concat(Observations("A", Layout(a1, RecipeA), RecipeA, "p1-"))
            .Concat(Observations("A", Layout(a2, RecipeA), RecipeA, "p2-"))
            .ToArray();

        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(
            [
                new RoofGeneratedRafterCopyOwnerTarget("A", a),
                new RoofGeneratedRafterCopyOwnerTarget("A1", a1),
                new RoofGeneratedRafterCopyOwnerTarget("A2", a2),
            ],
            observations);

        Assert.Equal(3, plan.Associations.Count);
        Assert.Contains(
            plan.Associations,
            item => item.OwnerReference == "A" && !item.RequiresMetadataRewrite);
        Assert.Contains(
            plan.Associations,
            item => item.OwnerReference == "A1" && item.RequiresMetadataRewrite);
        Assert.Contains(
            plan.Associations,
            item => item.OwnerReference == "A2" && item.RequiresMetadataRewrite);
    }

    [Fact]
    public void ReverseLineDirectionStillMatches()
    {
        var geometry = Geometry(0, 0);
        var layout = Layout(geometry, RecipeA);
        var expected = layout.Rafters[0];
        var actual = new RoofGeneratedRafterGeometryObservation(
            "rev",
            "A",
            RecipeA,
            expected.Face,
            expected.StationIndex,
            expected.StationCount,
            expected.PlanEnd,
            expected.PlanStart,
            layout.Signature);

        Assert.True(RoofGeneratedRafterCopyAssociationRules.GeometryMatches(expected, actual));
    }

    private static IEnumerable<RoofGeneratedRafterGeometryObservation> Observations(
        string owner,
        SimpleGableRafterLayout layout,
        RoofRafterGenerationRecipe recipe,
        string keyPrefix = "") =>
        layout.Rafters.Select((rafter, index) => new RoofGeneratedRafterGeometryObservation(
            $"{keyPrefix}{owner}-{index}",
            owner,
            recipe,
            rafter.Face,
            rafter.StationIndex,
            rafter.StationCount,
            rafter.PlanStart,
            rafter.PlanEnd,
            layout.Signature));

    private static SimpleGableRafterLayout Layout(
        SimpleGableRoofGeometry geometry,
        RoofRafterGenerationRecipe recipe)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static SimpleGableRoofGeometry Geometry(double originX, double originY)
    {
        var vertices = new[]
        {
            new RoofPoint2D(originX, originY),
            new RoofPoint2D(originX + 10000d, originY),
            new RoofPoint2D(originX + 10000d, originY + 8000d),
            new RoofPoint2D(originX, originY + 8000d),
        };
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(30d, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }
}
