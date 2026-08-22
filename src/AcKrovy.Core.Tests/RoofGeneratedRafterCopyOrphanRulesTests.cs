using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedRafterCopyOrphanRulesTests
{
    private static readonly RoofRafterGenerationRecipe RecipeA =
        new(80d, 160d, 900d, "Smrek C24");

    [Fact]
    public void SingleCopiedRafter_IsDetached_OriginalSetStaysClaimedAndUnique()
    {
        var original = Geometry(0, 0);
        var layout = Layout(original, RecipeA);
        var originals = Observations("291A", layout, RecipeA).ToArray();
        var source = originals[0];
        var clone = source with
        {
            MemberKey = "copy-1",
            PlanStart = new RoofPoint2D(source.PlanStart.X + 2500d, source.PlanStart.Y),
            PlanEnd = new RoofPoint2D(source.PlanEnd.X + 2500d, source.PlanEnd.Y),
        };
        var observations = originals.Concat([clone]).ToArray();
        var owners = new[] { new RoofGeneratedRafterCopyOwnerTarget("291A", original) };

        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);
        Assert.Single(plan.Associations);
        Assert.DoesNotContain(plan.Associations[0].Members, member => member.MemberKey == "copy-1");

        var detached = RoofGeneratedRafterCopyOrphanRules.FindAllStandaloneDetachMemberKeys(
            plan,
            owners,
            observations,
            ["copy-1"]);
        Assert.Equal(["copy-1"], detached);

        var remaining = observations
            .Where(item => !detached.Contains(item.MemberKey, StringComparer.Ordinal))
            .Select(ToGenerated)
            .ToArray();
        Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(remaining));
        Assert.Equal(originals.Length, remaining.Length);
    }

    [Fact]
    public void DuplicateStation_WithCompleteAssociation_DetachesNonClaimedClone_WithoutAppendedList()
    {
        var original = Geometry(0, 0);
        var layout = Layout(original, RecipeA);
        var originals = Observations("291A", layout, RecipeA).ToArray();
        var source = originals[0];
        var clone = source with
        {
            MemberKey = "copy-1",
            PlanStart = new RoofPoint2D(source.PlanStart.X + 2500d, source.PlanStart.Y),
            PlanEnd = new RoofPoint2D(source.PlanEnd.X + 2500d, source.PlanEnd.Y),
        };
        var observations = originals.Concat([clone]).ToArray();
        var owners = new[] { new RoofGeneratedRafterCopyOwnerTarget("291A", original) };
        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);
        Assert.Single(plan.Associations);

        var detached = RoofGeneratedRafterCopyOrphanRules.FindDuplicateStationDetachMemberKeys(
            plan,
            observations,
            appendedMemberKeys: null);
        Assert.Equal(["copy-1"], detached);
        Assert.DoesNotContain(source.MemberKey, detached);
    }

    [Fact]
    public void WholeRoofCopy_DoesNotDetachCopiedChildren()
    {
        var original = Geometry(0, 0);
        var copied = Geometry(20000, 0);
        var observations = Observations("291A", Layout(original, RecipeA), RecipeA)
            .Concat(Observations("291A", Layout(copied, RecipeA), RecipeA, "copy-"))
            .ToArray();
        var owners = new[]
        {
            new RoofGeneratedRafterCopyOwnerTarget("291A", original),
            new RoofGeneratedRafterCopyOwnerTarget("2996", copied),
        };
        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);
        Assert.Equal(2, plan.Associations.Count);

        var appended = observations
            .Where(item => item.MemberKey.StartsWith("copy-", StringComparison.Ordinal))
            .Select(item => item.MemberKey)
            .ToArray();
        var detached = RoofGeneratedRafterCopyOrphanRules.FindStandaloneDetachMemberKeys(
            plan,
            owners,
            observations,
            appended);
        Assert.Empty(detached);
    }

    [Fact]
    public void EditedMemberClone_DetachesByAppendedKey_EvenWhenCompleteMatchFails()
    {
        var original = Geometry(0, 0);
        var layout = Layout(original, RecipeA);
        var originals = Observations("291A", layout, RecipeA).ToArray();
        var edited = originals[0] with
        {
            PlanStart = new RoofPoint2D(originals[0].PlanStart.X + 180d, originals[0].PlanStart.Y + 40d),
            PlanEnd = new RoofPoint2D(originals[0].PlanEnd.X + 180d, originals[0].PlanEnd.Y + 40d),
        };
        originals[0] = edited;
        var clone = edited with
        {
            MemberKey = "copy-edited",
            PlanStart = new RoofPoint2D(edited.PlanStart.X + 4000d, edited.PlanStart.Y),
            PlanEnd = new RoofPoint2D(edited.PlanEnd.X + 4000d, edited.PlanEnd.Y),
        };
        var observations = originals.Concat([clone]).ToArray();
        var owners = new[] { new RoofGeneratedRafterCopyOwnerTarget("291A", original) };
        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);
        Assert.Empty(plan.Associations);

        var detached = RoofGeneratedRafterCopyOrphanRules.FindStandaloneDetachMemberKeys(
            plan,
            owners,
            observations,
            ["copy-edited"]);
        Assert.Equal(["copy-edited"], detached);
        Assert.DoesNotContain(originals[0].MemberKey, detached);
    }

    [Fact]
    public void UnrelatedAppendedKey_IsIgnored()
    {
        var original = Geometry(0, 0);
        var observations = Observations("291A", Layout(original, RecipeA), RecipeA).ToArray();
        var owners = new[] { new RoofGeneratedRafterCopyOwnerTarget("291A", original) };
        var plan = RoofGeneratedRafterCopyAssociationRules.BuildPlan(owners, observations);

        var detached = RoofGeneratedRafterCopyOrphanRules.FindStandaloneDetachMemberKeys(
            plan,
            owners,
            observations,
            ["unrelated-line"]);
        Assert.Empty(detached);
    }

    [Fact]
    public void DuplicateStationWithoutDetach_FailsUniqueStations()
    {
        var members = new[]
        {
            Member("291A", RafterRoofFace.Face0, 0),
            Member("291A", RafterRoofFace.Face0, 1),
            Member("291A", RafterRoofFace.Face0, 0),
        };
        Assert.False(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members));
        Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members.Take(2).ToArray()));
    }

    [Fact]
    public void SchemasStayUnchangedForStandaloneCopySemantics()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(7, AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion);
    }

    private static RoofGeneratedTimberData Member(
        string owner,
        RafterRoofFace face,
        int station) =>
        new(1, owner, RoofGeneratedTimberKind.Rafter, face, station, 4, 900d, "sig");

    private static RoofGeneratedTimberData ToGenerated(
        RoofGeneratedRafterGeometryObservation observation) =>
        new(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            observation.EffectiveOwnerReference,
            RoofGeneratedTimberKind.Rafter,
            observation.Face,
            observation.StationIndex,
            observation.StationCount,
            observation.Recipe.MaximumSpacingMm,
            observation.LayoutSignature);

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
