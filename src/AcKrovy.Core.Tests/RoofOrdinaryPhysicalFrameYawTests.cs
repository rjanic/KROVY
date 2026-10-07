using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryPhysicalFrameYawTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(-20)]
    [InlineData(45)]
    public void SlopedRoof_FinalYawPreservesProjectionTopPlaneSectionAndDirectedEnds(double yaw)
    {
        foreach (var pitch in new[] { 25d, 45d, 60d })
        foreach (var roofYaw in new[] { 0d, 37d })
        {
            var state = Fixture(pitch, roofYaw);
            var end = state.AcceptedPlanAxis.End;
            var angle = (yaw + roofYaw) * Math.PI / 180;
            var axis = new RoofSegment3D(new(end.X + 2200 * Math.Sin(angle),
                end.Y - 2200 * Math.Cos(angle), 0), end);
            Verify(state, axis);
        }
    }

    [Fact]
    public void ExactHostLine2941_RebuildsOnTheSameRoofPlane()
    {
        var state = Fixture(45, 0);
        Verify(state, new(new(43606.83008065798, 10391.85672258641, 0),
            new(42491.02099635819, 14478.696868240506, 0)));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    [InlineData(45)]
    public void ReversedDirectedAxis_DoesNotInvertHeightOrMirrorTheFrame(double yaw)
    {
        var state = Fixture(45, 37);
        var end = state.AcceptedPlanAxis.End;
        var angle = (yaw + 37) * Math.PI / 180;
        var start = new RoofPoint3D(end.X + 2200 * Math.Sin(angle), end.Y - 2200 * Math.Cos(angle), 0);
        Verify(state, new(end, start));
    }

    private static void Verify(RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, plan, out var member, out _, out var reason), reason);
        Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(state, member!, out var described));
        var frame = described!;
        Assert.Equal(plan, member!.PlanAxis);
        Assert.Equal(0, plan.Start.Z);
        Assert.Equal(0, plan.End.Z);
        Assert.Equal(plan.Start.X, frame.UpperAxis.Start.X);
        Assert.Equal(plan.Start.Y, frame.UpperAxis.Start.Y);
        Assert.Equal(plan.End.X, frame.UpperAxis.End.X);
        Assert.Equal(plan.End.Y, frame.UpperAxis.End.Y);
        var prism = member.HorizontalCut?.SourcePrismVertices ?? member.StructuralCut?.SourcePrismVertices ??
            member.RidgeOverlapCut?.SourcePrismVertices ?? member.RidgeMeetCut?.SourcePrismVertices ?? member.SolidVertices;
        Assert.Equal(8, prism.Count);
        var actualL = Unit(Sub(prism[2], prism[0]));
        var actualW = Unit(Sub(prism[1], prism[0]));
        var actualH = Unit(Cross(actualL, actualW));
        Assert.InRange(Dot(actualL, frame.LongitudinalAxis), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Dot(actualW, frame.SectionWidthAxis), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Dot(actualH, frame.RoofNormal), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Math.Abs(Dot(actualL, actualW)), 0, 1e-9);
        Assert.InRange(Dot(Cross(actualL, actualW), actualH), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80 - 1e-7, 80 + 1e-7);
        Assert.All(Enumerable.Range(0, 4), i => Assert.InRange(
            Dot(Sub(prism[i], prism[i + 4]), frame.RoofNormal), 160 - 1e-7, 160 + 1e-7));
        Assert.All(prism.Take(4), p => Assert.InRange(
            Math.Abs(Dot(Sub(p, frame.RoofPlaneOrigin), frame.RoofNormal)), 0, 1e-7));
        var finalTop = member.RidgeMeetCut?.TopFaceVertices ?? prism.Take(4).ToArray();
        Assert.All(finalTop, p => Assert.InRange(
            Math.Abs(Dot(Sub(p, frame.RoofPlaneOrigin), frame.RoofNormal)), 0, 1e-7));
        Assert.Equal(frame.UpperAxis.Start.DistanceTo(frame.UpperAxis.End), member.PhysicalLengthMm, 7);
        if (member.RidgeMeetCut is { } cut)
            Assert.All(cut.CutFaceVertices, p => Assert.InRange(
                Math.Abs(Dot(Sub(p, cut.PlanePoint), cut.RetainedNormal)), 0, 1e-7));
        // Width-centres of both source upper ends lie on the directed Plan line,
        // including source extensions needed for the approved final end cuts.
        var planDelta = Sub(plan.End, plan.Start);
        for (var i = 0; i < 4; i += 2)
        {
            var c = new RoofPoint3D((prism[i].X + prism[i + 1].X) / 2,
                (prism[i].Y + prism[i + 1].Y) / 2, 0);
            Assert.InRange(Math.Abs(planDelta.X * (c.Y - plan.Start.Y) -
                planDelta.Y * (c.X - plan.Start.X)) / plan.Start.DistanceTo(plan.End), 0, 1e-7);
        }
    }

    private static RoofOrdinaryPhysicalBuildState Fixture(double pitch, double roofYaw)
    {
        const double x = 36141.02099635819, y = 11478.696868240506;
        var angle = roofYaw * Math.PI / 180;
        RoofPoint2D Map(double px, double py) => new(x + px * Math.Cos(angle) - py * Math.Sin(angle),
            y + px * Math.Sin(angle) + py * Math.Cos(angle));
        var footprint = RoofFootprintValidator.Validate(new(
            [Map(0,0), Map(10000,0), Map(10000,6000), Map(0,6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(
            footprint.Footprint!, new(pitch), RoofKind.Hip)).Geometry);
        var slope = pitch * Math.PI / 180;
        var expectedNormal = new RoofPoint3D(Math.Sin(angle) * Math.Sin(slope),
            -Math.Cos(angle) * Math.Sin(slope), Math.Cos(slope));
        // Footprint normalization can renumber edges after rotating the roof.
        var face = roof.Topology.Faces.Single(f =>
            RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(roof.Topology, f, out var n) &&
            Dot(new(n.X, n.Y, n.Z), expectedNormal) > 1 - 1e-9);
        var anchor = RoofFaceRafterLayoutService.Create(roof.Topology, 900).Layout!.Segments.First(s =>
            s.SourceFaceIndex == face.SourceEdgeIndex && s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        var a = Map(6350, 0);
        var b = Map(6350, 3000);
        return RoofOrdinaryPhysicalBuildStateRules.Capture(roof.Topology, anchor,
            new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 6), 3000,
            80, 160, new(LowerEndCutMode.Vertical, RidgeJoinMode.Meet), [],
            new(new(a.X, a.Y, 0), new(b.X, b.Y, 0)));
    }

    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X);
    private static RoofPoint3D Unit(RoofPoint3D p) => new(p.X/Math.Sqrt(Dot(p,p)), p.Y/Math.Sqrt(Dot(p,p)), p.Z/Math.Sqrt(Dot(p,p)));
}
