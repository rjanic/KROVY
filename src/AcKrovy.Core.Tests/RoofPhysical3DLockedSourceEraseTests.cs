using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofPhysical3DLockedSourceEraseTests
{
    [Theory]
    [InlineData(true, true, false, false)] // Historical erase notification, restored source.
    [InlineData(true, true, true, true)]   // Intentional, still-erased source.
    [InlineData(false, true, true, false)] // Unknown provenance fails closed.
    [InlineData(true, false, true, false)] // Timber/annotation handle is not a roof owner.
    public void Cleanup_UsesCurrentSourceState(bool resolved, bool roof, bool erased, bool delete) =>
        Assert.Equal(delete, RoofPhysical3DSetRules.ShouldEraseForSourceState(resolved, roof, erased));

    [Theory]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    public void CompleteCopiedSet_SurvivesRejectedSourceErase_AndRepeatedPitchChecks(string command)
    {
        Assert.NotEmpty(command); // Shared policy; native command execution is HOST-only.
        var original = Model("2912", 30);
        var cloned = Model("293C", 30);
        var sourceSet = RoofPhysical3DSetRules.ExpectedChildren(original);
        var clonedSet = RoofPhysical3DSetRules.ExpectedChildren(cloned);
        Assert.Equal(13, clonedSet.Count);
        Assert.Equal(4, clonedSet.Count(child => child.Role == RoofPhysical3DGeneratedRole.Face));
        Assert.Equal(4, clonedSet.Count(child => child.Role == RoofPhysical3DGeneratedRole.EaveEdge));
        Assert.Equal(4, clonedSet.Count(child => child.Role == RoofPhysical3DGeneratedRole.HipEdge));
        Assert.Single(clonedSet, child => child.Role == RoofPhysical3DGeneratedRole.RidgeEdge);
        Assert.True(RoofPhysical3DSetRules.IsComplete(cloned, clonedSet));
        Assert.False(RoofPhysical3DSetRules.ShouldEraseForSourceState(true, true, false));
        foreach (var pitch in new[] { 30d, 45d, 30d })
        {
            var current = Model("293C", pitch);
            Assert.True(RoofPhysical3DSetRules.IsComplete(current, RoofPhysical3DSetRules.ExpectedChildren(current)));
            Assert.True(RoofPhysical3DSetRules.IsComplete(original, sourceSet));
            Assert.All(current.Eaves, edge => Assert.Equal(3000d, edge.Segment.Start.Z, 8));
            Assert.Equal(3000d + 3000d * Math.Tan(pitch * Math.PI / 180d), current.Ridges.Single().Segment.Start.Z, 8);
            Assert.Equal(4000d, current.Ridges.Single().Segment.LengthMm, 8);
        }
        Assert.False(RoofPhysical3DSetRules.IsComplete(Model("293C", 45), clonedSet));
    }

    [Theory]
    [InlineData(RoofPhysical3DGeneratedRole.Face)]
    [InlineData(RoofPhysical3DGeneratedRole.EaveEdge)]
    [InlineData(RoofPhysical3DGeneratedRole.HipEdge)]
    [InlineData(RoofPhysical3DGeneratedRole.RidgeEdge)]
    public void MissingChildInEveryRole_RequiresRepair(RoofPhysical3DGeneratedRole role)
    {
        var model = Model("293C", 30);
        var set = RoofPhysical3DSetRules.ExpectedChildren(model).ToList();
        set.Remove(set.First(child => child.Role == role));
        Assert.False(RoofPhysical3DSetRules.IsComplete(model, set));
        Assert.False(RoofPhysical3DSetRules.IsComplete(model, Array.Empty<RoofPhysical3DGeneratedData>()));
    }

    [Fact]
    public void DuplicateWrongOwnerOrSignature_RequiresRepairWithoutCrossOwnerAdoption()
    {
        var model = Model("293C", 30);
        var expected = RoofPhysical3DSetRules.ExpectedChildren(model).ToArray();
        var duplicate = expected.ToArray();
        duplicate[1] = duplicate[0];
        Assert.False(RoofPhysical3DSetRules.IsComplete(model, duplicate));
        var otherOwner = expected.ToArray();
        otherOwner[0] = otherOwner[0] with { RoofOwnerReference = "2912" };
        Assert.False(RoofPhysical3DSetRules.IsComplete(model, otherOwner));
        var stale = expected.ToArray();
        stale[0] = stale[0] with { GenerationSignature = "OLD" };
        Assert.False(RoofPhysical3DSetRules.IsComplete(model, stale));
        Assert.True(RoofPhysical3DSetRules.IsComplete(model, expected.Reverse().ToArray()));
    }

    private static RoofPhysical3DModel Model(string owner, double pitch)
    {
        var input = new RoofFootprintInput(new[]
        {
            new RoofPoint2D(0, 0), new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000), new RoofPoint2D(0, 6000),
        }, true);
        var footprint = RoofFootprintValidator.Validate(input).Footprint!;
        var geometry = (HipRoofGeometry)RoofGeometrySolver.Solve(new RoofDefinition(footprint,
            new RoofParameters(pitch), RoofKind.Hip)).Geometry!;
        var elevation = RoofAbsoluteElevationRules.FromEntered(RoofAbsoluteElevationInputMode.Eave,
            3000d, 3000d, pitch, true);
        return RectangularHipRoofPhysical3DBuilder.TryBuild(owner, footprint, geometry, elevation).Model!;
    }
}
