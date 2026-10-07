using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryGripLifecycleTests
{
    [Fact]
    public void HostFailedEndpoint_ClassifiesStart_AndBuildsNewPhysicalFrameWithoutOverrides()
    {
        var (state, before) = Fixture();
        var after = new RoofSegment3D(new(43234.902248851795,10749.501585525788,0), before.End);
        Assert.Equal(RoofOrdinaryGripChange.Start, RoofOrdinaryGripLifecycleRules.Classify(before, after));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, after, out var member, out var updated, out var reason), reason);
        Assert.Equal(after, member!.PlanAxis);
        Assert.Equal(after, updated!.AcceptedPlanAxis);
        Assert.Equal(state.MemberKey, member.MemberKey);
        Assert.Equal(80, member.WidthMm);
        Assert.Equal(160, member.HeightMm);
        Assert.Equal(RoofRafterBoundaryRole.Ridge, member.EndBoundaryRole);
        Assert.NotNull(member.RidgeMeetCut);
        var prism = member.RidgeMeetCut!.SourcePrismVertices;
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80 - 1e-7, 80 + 1e-7);
        Assert.True(Math.Abs(prism[0].X - prism[2].X) > 100);
        // Roof face 0 is y-up, pitch 45, eave Z=3000. All final top vertices lie on it.
        Assert.All(member.RidgeMeetCut.TopFaceVertices, p =>
            Assert.InRange(Math.Abs(p.Z - (3000 + p.Y - 11478.696868240506)), 0, 1e-7));
    }

    [Fact]
    public void IndependentRepeatedEndpointEdit_UsesPersistedFullState()
    {
        var (state, before) = Fixture();
        var first = new RoofSegment3D(new(43234.902248851795,10749.501585525788,0), before.End);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, first, out _, out var accepted, out var reason), reason);
        var persisted = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(accepted))!;
        var next = new RoofSegment3D(first.Start, new(first.End.X - 450, first.End.Y - 400, 0));
        Assert.Equal(RoofOrdinaryGripChange.End, RoofOrdinaryGripLifecycleRules.Classify(first, next));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(persisted, next, out var body, out _, out reason), reason);
        Assert.Equal(next, body!.PlanAxis);
        Assert.Equal(state.WidthMm, body.WidthMm);
        Assert.Equal(state.HeightMm, body.HeightMm);
        Assert.Equal(state.Settings, persisted.Settings);
    }

    [Fact]
    public void MiddleGrip_RebasesFullPlaneAndCuts_AsOneRigidTranslation()
    {
        var (state, before) = Fixture();
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, before, out var original, out _, out var reason), reason);
        var after = new RoofSegment3D(new(before.Start.X + 1700, before.Start.Y - 625, 0),
            new(before.End.X + 1700, before.End.Y - 625, 0));
        Assert.Equal(RoofOrdinaryGripChange.Middle, RoofOrdinaryGripLifecycleRules.Classify(before, after));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, after, out var moved));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(moved!, after, out var body, out _, out reason), reason);
        Assert.Equal(original!.SolidVertices.Count, body!.SolidVertices.Count);
        for (var i = 0; i < original.SolidVertices.Count; i++)
            Assert.InRange(new RoofPoint3D(original.SolidVertices[i].X + 1700,
                original.SolidVertices[i].Y - 625, original.SolidVertices[i].Z)
                .DistanceTo(body.SolidVertices[i]), 0, 1e-7);
        Assert.Equal(original.PhysicalLengthMm, body.PhysicalLengthMm, 7);
        Assert.Equal(original.RidgeMeetCut is null, body.RidgeMeetCut is null);
    }

    [Fact]
    public void NonRigidRebaseOrNonPlanarAcceptedAxis_FailsClosed()
    {
        var (state, before) = Fixture();
        var after = new RoofSegment3D(new(before.Start.X + 1200, before.Start.Y, 0), before.End);
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, after, out _));
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state,
            new(new(before.Start.X, before.Start.Y, 100), before.End), out _, out _, out _));
        Assert.Equal(0, RoofOrdinaryGripLifecycleRules.Plan(
            new(new(before.Start.X, before.Start.Y, 100), before.End)).Start.Z);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Overlap)]
    public void BuilderState_RoundtripPreservesPhysicalCutContract(LowerEndCutMode lower, RidgeJoinMode ridge)
    {
        var (state, before) = Fixture(35);
        state = state with { Settings = new(lower, ridge) };
        var decoded = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(state))!;
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(decoded, before, out var member, out var updated, out var reason), reason);
        Assert.Equal(state.Settings, updated!.Settings);
        Assert.Equal(before, member!.PlanAxis);
        if (lower == LowerEndCutMode.Horizontal) Assert.NotNull(member.HorizontalCut);
        if (ridge == RidgeJoinMode.Overlap) Assert.NotNull(member.RidgeOverlapCut);
    }

    [Fact]
    public void DirectPhysicalGrip_AlwaysRestoresDerivedRepresentation()
    {
        foreach (var authority in Enum.GetValues<RoofOrdinaryGeometryAuthority>())
            Assert.Equal(RoofOrdinaryEditDecision.RestoreDerivedPhysical,
                RoofOrdinaryAuthorityTransitionRules.Decide(authority, "GRIP_STRETCH", true, false, false,
                    RoofOrdinaryEditRepresentation.Physical3D));
    }

    private static (RoofOrdinaryPhysicalBuildState State, RoofSegment3D Before) Fixture(double pitch = 45)
    {
        const double x = 36141.02099635819, y = 11478.696868240506;
        var footprint = RoofFootprintValidator.Validate(new(
            [new(x,y), new(x + 10000,y), new(x + 10000,y + 6000), new(x,y + 6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(
            footprint.Footprint!, new(pitch), RoofKind.Hip)).Geometry);
        var layout = RoofFaceRafterLayoutService.Create(roof.Topology, 900).Layout!;
        var before = new RoofSegment3D(new(41591.02099635819,y,0), new(41591.02099635819,y+3000,0));
        var anchor = layout.Segments.First(s => s.SourceFaceIndex == 0 && s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 6);
        return (RoofOrdinaryPhysicalBuildStateRules.Capture(roof.Topology, anchor, key, 3000,
            80,160,new(), Array.Empty<RoofStructuralRafterTrimSource>(), before), before);
    }
}
