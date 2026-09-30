using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Semantic replay and derived geometry; not native HOST Undo/Redo proof.</summary>
public sealed class RoofOrdinaryPhysicalStretchTests
{
    [Theory]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("_.STRETCH")]
    [InlineData("_.GRIP_STRETCH")]
    public void AcceptedEndpointEdits_SharePhysicalReconcile(string command) =>
        Assert.True(RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(command));

    [Theory]
    [InlineData("BREAK")]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    public void CardinalityChangesAndUndo_DoNotEnterGeneratedOnlyPhysicalReconcile(string command) =>
        Assert.False(RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(command));

    [Fact]
    public void MultiMemberEndpointReplay_PreservesIdentityAndOtherBodies_OnRoofPlane()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(35), RoofKind.Hip));
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faceResult = RoofFaceRafterLayoutService.Create(geometry.Topology, 500);
        Assert.True(faceResult.IsValid);
        var faceLayout = faceResult.Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, faceLayout, 80, out var generated));
        var settings = new RoofAutomaticRafterPhysicalSettings();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, faceLayout, generated, 3000, 80, 125,
            settings, out var before));
        var edited = generated.Rafters.Where(rafter =>
            faceLayout.Segments[rafter.StationIndex].EndBoundaryRole == RoofRafterBoundaryRole.Ridge)
            .Take(2).ToArray();
        Assert.Equal(2, edited.Length);
        var edits = edited.Select(rafter => new RoofGeneratedMemberOverride(
            rafter.LogicalKey, false, 0, 0, 0, 0, -125)).ToArray();
        var replay = RoofGeneratedMemberReplayPlanner.Create(generated, 0,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, edits);
        Assert.True(replay.IsValid);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, faceLayout, generated, 3000, 80, 125,
            settings, replay, out var after));
        Assert.Equal(before!.Members.Select(member => member.MemberKey),
            after!.Members.Select(member => member.MemberKey));
        Assert.Equal(after.Members.Count, after.Members.Select(member => member.MemberKey).Distinct().Count());
        foreach (var member in after.Members)
        {
            var old = before.Members.Single(item => item.MemberKey == member.MemberKey);
            Assert.Equal(0, member.PlanAxis.Start.Z);
            Assert.Equal(0, member.PlanAxis.End.Z);
            if (!edited.Any(rafter => rafter.LogicalKey == member.MemberKey))
            {
                Assert.Equal(old.PlanAxis, member.PlanAxis);
                Assert.Equal(old.SolidVertices, member.SolidVertices);
                continue;
            }
            Assert.NotEqual(old.PlanAxis, member.PlanAxis);
            Assert.True(member.PhysicalLengthMm < old.PhysicalLengthMm);
            var face = geometry.Topology.Faces.Single(item => item.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(geometry.Topology, face, out var normal));
            var origin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
            Assert.All(member.SolidVertices.Take(4), point => Assert.Equal(0,
                normal.X * (point.X - origin.X) + normal.Y * (point.Y - origin.Y) +
                normal.Z * (point.Z - origin.Z - 3000), 5));
        }
    }
}
