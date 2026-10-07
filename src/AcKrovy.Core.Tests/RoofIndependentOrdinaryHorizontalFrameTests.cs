using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofIndependentOrdinaryHorizontalFrameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyRebasesFullMemberContext_AndUsesHorizontalSectionForAutoAndIndependent(bool independent)
    {
        var seed = Fixture(35, 37);
        var sourcePlan = Yaw(seed, 20, 37);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(seed, sourcePlan,
            out var original, out var source, out var reason, useIndependentHorizontalFrame: independent), reason);
        var sourceJson = JsonSerializer.Serialize(source);
        RoofPoint3D Shift(RoofPoint3D p) => new(p.X + 16000, p.Y - 7000, p.Z);
        var copyPlan = new RoofSegment3D(Shift(sourcePlan.Start), Shift(sourcePlan.End));
        Assert.True(RoofOrdinaryCopyCloneRules.TryPreparePhysicalCopy(source!, copyPlan, out var copy));
        var (body, updated) = Build(copy!, copyPlan);
        Verify(copy!, copyPlan, body, updated);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(source));
        Assert.Equal(original!.PhysicalLengthMm, body.PhysicalLengthMm, 7);
        Assert.Equal(source!.Settings, updated.Settings);
        Assert.Equal(source.WidthMm, updated.WidthMm);
        Assert.Equal(source.HeightMm, updated.HeightMm);
        Assert.Equal(copyPlan, updated.AcceptedPlanAxis);
        if (independent)
        {
            Assert.Equal(original.SolidVertices.Count, body.SolidVertices.Count);
            for (var i = 0; i < original.SolidVertices.Count; i++)
                Assert.InRange(Shift(original.SolidVertices[i]).DistanceTo(body.SolidVertices[i]), 0, 1e-7);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BreakNativeResults_BuildSeparatePiecesIncludingZeroGapAndRepeatedSplit(bool zeroGap)
    {
        var source = Fixture(35, 37);
        RoofPoint3D At(double fraction) => new(
            source.AcceptedPlanAxis.Start.X + fraction * (source.AcceptedPlanAxis.End.X - source.AcceptedPlanAxis.Start.X),
            source.AcceptedPlanAxis.Start.Y + fraction * (source.AcceptedPlanAxis.End.Y - source.AcceptedPlanAxis.Start.Y), 0);
        var pieces = new[] { new RoofSegment3D(At(0), At(0.3)), new RoofSegment3D(At(zeroGap ? 0.3 : 0.6), At(1)) };
        Assert.True(RoofOrdinaryTrimSplitRules.IsSplit(source.AcceptedPlanAxis, pieces, allowTouchingPieces: true));
        var bodies = pieces.Select(piece => Build(source, piece)).ToArray();
        for (var i = 0; i < pieces.Length; i++) Verify(source, pieces[i], bodies[i].Item1, bodies[i].Item2);
        Assert.NotEqual(bodies[0].Item2.AcceptedPlanAxis, bodies[1].Item2.AcceptedPlanAxis);
        // Repeated BREAK of one Independent result derives two more bodies from its own state.
        var repeated = new[] { new RoofSegment3D(At(0), At(0.1)), new RoofSegment3D(At(0.1), At(0.3)) };
        Assert.True(RoofOrdinaryTrimSplitRules.IsSplit(pieces[0], repeated, allowTouchingPieces: true));
        foreach (var piece in repeated)
        {
            var (body, updated) = Build(bodies[0].Item2, piece);
            Verify(bodies[0].Item2, piece, body, updated);
            Assert.True(body.PhysicalLengthMm < bodies[0].Item1.PhysicalLengthMm);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtendFinalEndpoint_RebuildsLongerPhysicalBodyAndKeepsHorizontalSection(bool extendStart)
    {
        var seed = Fixture(35, 37);
        RoofPoint3D At(double fraction) => new(
            seed.AcceptedPlanAxis.Start.X + fraction * (seed.AcceptedPlanAxis.End.X - seed.AcceptedPlanAxis.Start.X),
            seed.AcceptedPlanAxis.Start.Y + fraction * (seed.AcceptedPlanAxis.End.Y - seed.AcceptedPlanAxis.Start.Y), 0);
        var before = new RoofSegment3D(At(0.2), At(0.8));
        var (original, state) = Build(seed, before);
        var final = extendStart ? before with { Start = At(0.05) } : before with { End = At(0.95) };
        var (extended, updated) = Build(state, final);
        Assert.Equal(extendStart ? RoofOrdinaryGripChange.Start : RoofOrdinaryGripChange.End,
            RoofOrdinaryGripLifecycleRules.Classify(before, final));
        Assert.True(extended.PhysicalLengthMm > original.PhysicalLengthMm);
        Assert.Equal(final, extended.PlanAxis);
        Assert.Equal(before, state.AcceptedPlanAxis);
        Assert.Equal(state.SectionFrame, updated.SectionFrame);
        Verify(state, final, extended, updated);
    }

    [Theory]
    [InlineData(45, 0)]
    [InlineData(35, 37)]
    public void MiddleTrimSplit_BuildsTwoSeparatePhysicalPiecesFromOneOriginalState(double pitch, double roofYaw)
    {
        var source = Fixture(pitch, roofYaw);
        RoofPoint3D At(double fraction) => new(
            source.AcceptedPlanAxis.Start.X + fraction * (source.AcceptedPlanAxis.End.X - source.AcceptedPlanAxis.Start.X),
            source.AcceptedPlanAxis.Start.Y + fraction * (source.AcceptedPlanAxis.End.Y - source.AcceptedPlanAxis.Start.Y), 0);
        var pieces = new[] { new RoofSegment3D(At(0), At(0.3)), new RoofSegment3D(At(0.6), At(1)) };
        Assert.True(RoofOrdinaryTrimSplitRules.IsSplit(source.AcceptedPlanAxis, pieces));
        var bodies = pieces.Select(piece => Build(source, piece)).ToArray();
        for (var i = 0; i < pieces.Length; i++) Verify(source, pieces[i], bodies[i].Item1, bodies[i].Item2);
        Assert.NotEqual(bodies[0].Item1.PhysicalLengthMm, bodies[1].Item1.PhysicalLengthMm);
        Assert.Equal(source.AcceptedPlanAxis, Fixture(pitch, roofYaw).AcceptedPlanAxis);
        Assert.NotEqual(bodies[0].Item2.AcceptedPlanAxis, bodies[1].Item2.AcceptedPlanAxis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(-20)]
    [InlineData(40)]
    public void DetachedYaw_UsesHorizontalWidthAndUpwardHeightWithoutRoofRoll(double yaw)
    {
        foreach (var pitch in new[] { 25d, 45d, 60d })
        foreach (var roofYaw in new[] { 0d, 37d })
        {
            var state = Fixture(pitch, roofYaw);
            var plan = Yaw(state, yaw, roofYaw);
            var (member, updated) = Build(state, plan);
            Verify(state, plan, member, updated);
            if (yaw != 0)
            {
                Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(state, member, out var reference));
                Assert.True(Dot(member.SectionOrientation!.NewFrame.HeightAxis, reference!.RoofNormal) < 1 - 1e-5);
                Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, plan,
                    out var auto, out _, out var reason), reason);
                // AUTO still uses its approved roof frame. This assertion fails for
                // the old Independent roof-normal oracle and detects the reported roll.
                Assert.True(Math.Abs(Sub(Prism(auto!)[1], Prism(auto!)[0]).Z) > 0.01);
            }
        }
    }

    [Fact]
    public void ExactHost293F_ChangesTheReportedTiltedWidthToHorizontal()
    {
        var state = Fixture();
        var plan = new RoofSegment3D(new(42132.20804897987,11662.691030875314,0),
            new(40691.02099635819,14478.696868240506,0));
        var (member, updated) = Build(state, plan);
        Verify(state, plan, member, updated);
        var orientation = member.SectionOrientation!;
        Assert.Equal(0, orientation.NewFrame.WidthAxis.Z);
        // The minimum transport reference reproduces the rejected HOST frame.
        Assert.InRange(orientation.MinimumTransportFrame.WidthAxis.Z, -0.240620651, -0.240620650);
        Assert.InRange(orientation.MinimumTransportAngleDegrees, 19.89448172, 19.89448174);
        Assert.True(Math.Abs(RoofOrdinarySectionTransportRules.ExtraTwistDegrees(
            orientation.MinimumTransportFrame, orientation.NewFrame)) > 10);
        var expectedDirection = Unit(new RoofPoint3D(-1441.1870526216808,2816.005837365192,2816.005837365192));
        Assert.InRange(orientation.NewFrame.LongitudinalAxis.DistanceTo(expectedDirection), 0, 1e-9);
    }

    [Fact]
    public void RepeatedIndependentYaw_RoundtripAndRigidMoveKeepMemberFrame()
    {
        var state = Fixture();
        foreach (var yaw in new[] { 20d, -20d, 40d, 0d })
        {
            var plan = Yaw(state, yaw, 0);
            var (member, updated) = Build(state, plan);
            Verify(state, plan, member, updated);
            state = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(updated))!;
            Assert.Equal(member.SectionOrientation!.NewFrame, state.SectionFrame);
            Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(state));
        }
        var (original, _) = Build(state, state.AcceptedPlanAxis);
        RoofPoint3D Shift(RoofPoint3D p) => new(p.X + 1700, p.Y - 625, p.Z);
        var movedPlan = new RoofSegment3D(Shift(state.AcceptedPlanAxis.Start), Shift(state.AcceptedPlanAxis.End));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, movedPlan, out var moved));
        var (translated, saved) = Build(moved!, movedPlan);
        Assert.False(translated.SectionOrientation!.DirectionChanged);
        Assert.Equal(state.SectionFrame, saved.SectionFrame);
        Assert.Equal(original.SolidVertices.Count, translated.SolidVertices.Count);
        for (var i = 0; i < original.SolidVertices.Count; i++)
            Assert.InRange(Shift(original.SolidVertices[i]).DistanceTo(translated.SolidVertices[i]), 0, 1e-7);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Overlap)]
    public void FullEndCutSolver_UsesTheIndependentSection(LowerEndCutMode lower, RidgeJoinMode ridge)
    {
        var state = Fixture(35) with { Settings = new(lower, ridge) };
        var end = state.AcceptedPlanAxis.End;
        var plan = new RoofSegment3D(new(end.X + 3000 * Math.Tan(20 * Math.PI / 180),
            state.AcceptedPlanAxis.Start.Y, 0), end);
        var (member, updated) = Build(state, plan);
        Verify(state, plan, member, updated);
        if (lower == LowerEndCutMode.Horizontal) Assert.NotNull(member.HorizontalCut);
        if (ridge == RidgeJoinMode.Overlap) Assert.NotNull(member.RidgeOverlapCut);
    }

    [Fact]
    public void LegacyStateAdoption_InvalidFrameAndWorldVerticalDirection()
    {
        var state = Fixture() with { Version = 1, SectionFrame = null };
        var (member, updated) = Build(state, Yaw(state, 20, 0));
        Assert.Equal(2, updated.Version);
        Assert.Equal(member.SectionOrientation!.NewFrame, updated.SectionFrame);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(state));
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.IsValid(state with
        { SectionFrame = new(new(1,0,0), new(1,0,0), new(0,0,1)) }));
        Assert.False(RoofOrdinaryHorizontalSectionFrameRules.TryCreate(new(0,0,1), out _));
        Assert.False(RoofOrdinaryHorizontalSectionFrameRules.TryCreate(new(double.NaN,0,1), out _));
    }

    [Fact]
    public void ReversingDirectedAxis_PreservesUpwardHeightAndRightHandedness()
    {
        var state = Fixture();
        var plan = new RoofSegment3D(state.AcceptedPlanAxis.End, state.AcceptedPlanAxis.Start);
        var (member, updated) = Build(state, plan);
        Verify(state, plan, member, updated);
        Assert.True(member.SectionOrientation!.NewFrame.HeightAxis.Z > 0);
    }

    private static void Verify(RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan,
        RoofAutomaticRafterPhysicalMember member, RoofOrdinaryPhysicalBuildState updated)
    {
        Assert.Equal(plan, member.PlanAxis);
        Assert.Equal(0, member.PlanAxis.Start.Z);
        Assert.Equal(0, member.PlanAxis.End.Z);
        Assert.Equal(state.MemberKey, member.MemberKey);
        Assert.Equal(80, member.WidthMm);
        Assert.Equal(160, member.HeightMm);
        var orientation = member.SectionOrientation!;
        Assert.Equal(state.SectionFrame ?? orientation.OldFrame, orientation.OldFrame);
        var prism = Prism(member);
        var l = Unit(Sub(prism[2], prism[0]));
        var w = Unit(Sub(prism[1], prism[0]));
        var h = Unit(Cross(l, w));
        Assert.InRange(w.Z, -1e-9, 1e-9);
        Assert.True(h.Z > 0);
        Assert.InRange(Math.Abs(Dot(l, w)), 0, 1e-9);
        Assert.InRange(Dot(Cross(l, w), h), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(l.DistanceTo(orientation.NewFrame.LongitudinalAxis), 0, 1e-9);
        Assert.InRange(w.DistanceTo(orientation.NewFrame.WidthAxis), 0, 1e-9);
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80 - 1e-7, 80 + 1e-7);
        for (var i = 0; i < 4; i++)
            Assert.InRange(Dot(Sub(prism[i], prism[i + 4]), h), 160 - 1e-7, 160 + 1e-7);
        Assert.InRange(Math.Abs(RoofOrdinarySectionTransportRules.ExtraTwistDegrees(
            orientation.NewFrame, new(l,w,h))), 0, 1e-7);
        // Independent end cuts remain the existing geometric half-spaces.
        if (member.RidgeMeetCut is { } meet)
            Assert.All(meet.CutFaceVertices, p => Assert.InRange(
                Math.Abs(Dot(Sub(p, meet.PlanePoint), meet.RetainedNormal)), 0, 1e-7));
        var delta = Sub(plan.End, plan.Start);
        foreach (var i in new[] { 0, 2 })
        {
            var c = new RoofPoint3D((prism[i].X + prism[i+1].X) / 2, (prism[i].Y + prism[i+1].Y) / 2, 0);
            Assert.InRange(Math.Abs(delta.X * (c.Y-plan.Start.Y) - delta.Y * (c.X-plan.Start.X)) /
                plan.Start.DistanceTo(plan.End), 0, 1e-7);
        }
        Assert.Equal(orientation.NewFrame, updated.SectionFrame);
    }

    private static (RoofAutomaticRafterPhysicalMember, RoofOrdinaryPhysicalBuildState) Build(
        RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, plan,
            out var member, out var updated, out var reason, useIndependentHorizontalFrame: true), reason);
        return (member!, updated!);
    }

    private static RoofSegment3D Yaw(RoofOrdinaryPhysicalBuildState state, double yaw, double roofYaw)
    {
        var angle = (yaw + roofYaw) * Math.PI / 180;
        var end = state.AcceptedPlanAxis.End;
        return new(new(end.X + 2200 * Math.Sin(angle), end.Y - 2200 * Math.Cos(angle), 0), end);
    }

    internal static RoofOrdinaryPhysicalBuildState Fixture(double pitch = 45, double roofYaw = 0)
    {
        const double x = 36141.02099635819, y = 11478.696868240506;
        var angle = roofYaw * Math.PI / 180;
        RoofPoint2D Map(double px, double py) => new(x + px*Math.Cos(angle)-py*Math.Sin(angle),
            y + px*Math.Sin(angle)+py*Math.Cos(angle));
        var footprint = RoofFootprintValidator.Validate(new([Map(0,0), Map(10000,0), Map(10000,6000), Map(0,6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint.Footprint!, new(pitch), RoofKind.Hip)).Geometry);
        var slope = pitch * Math.PI / 180;
        var n = new RoofPoint3D(Math.Sin(angle)*Math.Sin(slope), -Math.Cos(angle)*Math.Sin(slope), Math.Cos(slope));
        var face = roof.Topology.Faces.Single(f => RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(roof.Topology, f, out var fn) &&
            Dot(new(fn.X,fn.Y,fn.Z), n) > 1 - 1e-9);
        var anchor = RoofFaceRafterLayoutService.Create(roof.Topology, 900).Layout!.Segments.First(s =>
            s.SourceFaceIndex == face.SourceEdgeIndex && s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        var a = Map(4550,0); var b = Map(4550,3000);
        var state = RoofOrdinaryPhysicalBuildStateRules.Capture(roof.Topology, anchor,
            new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 5), 3000, 80,160,new(),[],
            new(new(a.X,a.Y,0),new(b.X,b.Y,0)));
        Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(state, state.AcceptedPlanAxis, face.SourceEdgeIndex, out var frame));
        return state with { SectionFrame = new(frame!.LongitudinalAxis, frame.SectionWidthAxis, frame.SectionHeightAxis) };
    }

    private static IReadOnlyList<RoofPoint3D> Prism(RoofAutomaticRafterPhysicalMember m) =>
        m.HorizontalCut?.SourcePrismVertices ?? m.StructuralCut?.SourcePrismVertices ??
        m.RidgeOverlapCut?.SourcePrismVertices ?? m.RidgeMeetCut?.SourcePrismVertices ?? m.SolidVertices;
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static RoofPoint3D Unit(RoofPoint3D p) => new(p.X/Math.Sqrt(Dot(p,p)),p.Y/Math.Sqrt(Dot(p,p)),p.Z/Math.Sqrt(Dot(p,p)));
}
