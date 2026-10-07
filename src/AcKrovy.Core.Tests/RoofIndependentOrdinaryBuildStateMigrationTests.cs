using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofIndependentOrdinaryBuildStateMigrationTests
{
    [Fact]
    public void Host2942_MovedHipEndPackageRecoversWithoutHistoricalRoofBoundary()
    {
        var seed = Host2942State();
        var current = seed.AcceptedPlanAxis;
        var (body, _, frame, top, upper) = Package(seed, current);
        Assert.NotNull(body.StructuralCut);
        Assert.Equal(RoofRafterBoundaryRole.Hip, body.EndBoundaryRole);
        var unrelatedOwner = Seed();
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(
            unrelatedOwner with { SectionFrame = frame }, current, body.SolidVertices));
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(current, 80, 160,
            frame, upper, top, body.SolidVertices, seed.MemberKey, unrelatedOwner,
            out var recovered, out var reason), reason);
        Assert.Equal("MeasuredMemberStructuralCutsVerified", reason);
        Assert.Equal(current, recovered!.AcceptedPlanAxis);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(recovered, current, body.SolidVertices));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuredOffsetCutAtEitherDirectedEnd_RoundtripLengthenStretchRotateAndMove(bool reverse)
    {
        var seed = Host2942State();
        if (reverse) seed = seed with { AcceptedPlanAxis=new(seed.AcceptedPlanAxis.End,seed.AcceptedPlanAxis.Start) };
        var plan = seed.AcceptedPlanAxis;
        var (body,_,frame,top,upper) = Package(seed,plan);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(plan,80,160,frame,upper,top,
            body.SolidVertices,seed.MemberKey,null,out var recovered,out var reason),reason);
        var id = "369c1cdb71ad488f870b272942bf670f";
        var identity = new RoofIndependentOrdinaryTimberData(1,id,RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
            RoofIndependentOrdinaryEntityRole.PlanLine,"2912",seed.MemberKey);
        var payload = RoofIndependentOrdinaryTimberDataCodec.Encode(identity);
        var state = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(recovered))!;
        // LENGTHEN of the free/eave end keeps the measured offset cut at the other end.
        var final = reverse ? plan with { End=new(plan.End.X,plan.End.Y-200,0) }
            : plan with { Start=new(plan.Start.X,plan.Start.Y-200,0) };
        Assert.True(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(plan,final,out reason),reason);
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state,final,out _));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state,final,out var physical,out var next,out reason,
            useIndependentHorizontalFrame:true),reason);
        Assert.NotNull(physical!.StructuralCut);
        foreach (var edit in new[] { "STRETCH","ROTATE" })
        {
            state = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(next))!;
            if (edit == "STRETCH")
                final = final with { Start=new(final.Start.X-150,final.Start.Y-100,0) };
            else
            {
                var before = final;
                var dx = final.End.X-final.Start.X;
                var dy = final.End.Y-final.Start.Y;
                var angle = 25*Math.PI/180;
                final = new(final.Start,new(final.Start.X+dx*Math.Cos(angle)-dy*Math.Sin(angle),
                    final.Start.Y+dx*Math.Sin(angle)+dy*Math.Cos(angle),0));
                Assert.True(RoofOrdinaryRotateRules.IsRigidPlanRotation(before,final));
            }
            Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state,final,out physical,out next,out reason,
                useIndependentHorizontalFrame:true),reason);
            Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(next!,final,out var persisted));
            Assert.Equal(2,persisted!.Version); Assert.Equal(final,persisted.AcceptedPlanAxis);
            Assert.Equal(80,physical!.WidthMm); Assert.Equal(160,physical.HeightMm);
            Assert.InRange(Math.Abs(persisted.SectionFrame!.WidthAxis.Z),0,1e-9);
            Assert.Equal(payload,RoofIndependentOrdinaryTimberDataCodec.Encode(identity));
        }
        var moved = new RoofSegment3D(new(final.Start.X+500,final.Start.Y-300,0),new(final.End.X+500,final.End.Y-300,0));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryRebase(next!,moved,out var shifted));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(shifted!,moved,out _,out _,out reason,
            useIndependentHorizontalFrame:true),reason);
    }

    [Theory]
    [InlineData("tampered_body")]
    [InlineData("wrong_width")]
    [InlineData("rolled_frame")]
    [InlineData("no_plan")]
    public void MeasuredCutMigrationStillRejectsIncoherentPackages(string problem)
    {
        var seed = Host2942State();
        var plan = seed.AcceptedPlanAxis;
        var (body,_,frame,top,upper) = Package(seed,plan);
        var vertices = body.SolidVertices.ToArray();
        if (problem == "tampered_body") vertices[0] = vertices[0] with { X=vertices[0].X+5 };
        if (problem == "rolled_frame") frame = frame with { WidthAxis=new(0,0,1) };
        if (problem == "no_plan") plan = plan with { End=plan.Start };
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(plan,
            problem == "wrong_width" ? 100 : 80,160,frame,upper,top,vertices,seed.MemberKey,null,out _,out _));
    }

    [Fact]
    public void Host2Afc_NonRigidLegacyPackageRecoversAndSupportsStretchWithoutAutoStation()
    {
        var seed = Seed();
        var current = new RoofSegment3D(new(41164.03197439945,11576.697238304565,0),
            new(40565.94405622872,14478.696868240506,0));
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(seed, current, out _));
        var (body, oldState, frame, top, upper) = Package(seed, current);
        // Deliberately no persistent state and no live AUTO slot/context.
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(current, 80, 160,
            frame, upper, top, body.SolidVertices, seed.MemberKey, null, out var recovered, out var reason), reason);
        Assert.Equal("MeasuredMemberPlaneAndCutsVerified", reason);
        Assert.Equal(current, recovered!.AcceptedPlanAxis);
        Assert.Equal(2, recovered.Version);
        Assert.Equal(frame, recovered.SectionFrame);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(recovered, current, body.SolidVertices));
        var edited = current with { Start = new(41164.03197439945,11150.024332670262,0) };
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(recovered, edited,
            out var rebuilt, out var accepted, out reason, useIndependentHorizontalFrame: true), reason);
        Assert.Equal(edited, rebuilt!.PlanAxis);
        Assert.Equal(80, rebuilt.WidthMm); Assert.Equal(160, rebuilt.HeightMm);
        Assert.Equal(edited, accepted!.AcceptedPlanAxis);
        Assert.Equal(current, oldState.AcceptedPlanAxis);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void SafeRetainedCutsAreAdoptedAgainstCurrentPackage_WithoutRigidProvenanceRebase(LowerEndCutMode lower)
    {
        var seed = Seed() with { Settings = new(lower, RidgeJoinMode.Meet) };
        var current = seed.AcceptedPlanAxis with { Start = new(seed.AcceptedPlanAxis.Start.X + 150,
            seed.AcceptedPlanAxis.Start.Y, 0) };
        var (body, _, frame, top, upper) = Package(seed, current);
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(seed, current, out _));
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(current, 80, 160,
            frame, upper, top, body.SolidVertices, seed.MemberKey, seed, out var recovered, out var reason), reason);
        Assert.Equal("RetainedContextVerifiedAgainstCurrentPackage", reason);
        Assert.Equal(seed.Settings, recovered!.Settings);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(recovered, current, body.SolidVertices));
    }

    [Fact]
    public void ChangedRoofPlaneCannotReorientIndependentMigration()
    {
        var seed = Seed();
        var current = seed.AcceptedPlanAxis with { Start = new(seed.AcceptedPlanAxis.Start.X + 350,
            seed.AcceptedPlanAxis.Start.Y + 200, 0) };
        var (body, _, frame, top, upper) = Package(seed, current);
        var unrelatedRoof = seed with { Nodes = seed.Nodes.Select(p => new RoofPoint3D(p.X, p.Y, p.Z * 0.3)).ToArray(), PitchDegrees = 16 };
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(current, 80, 160,
            frame, upper, top, body.SolidVertices, seed.MemberKey, unrelatedRoof, out var recovered, out var reason), reason);
        Assert.Equal("MeasuredMemberPlaneAndCutsVerified", reason);
        Assert.Equal(frame, recovered!.SectionFrame);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(recovered, current, body.SolidVertices));
    }

    [Fact]
    public void SaveReopen_UsesSameV2ModelAndRemainsEditableWithNoMigration()
    {
        var seed = Seed();
        var current = seed.AcceptedPlanAxis with { Start = new(seed.AcceptedPlanAxis.Start.X + 300,
            seed.AcceptedPlanAxis.Start.Y + 200,0) };
        var (body, _, frame, top, upper) = Package(seed, current);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(current, 80,160,frame,upper,top,
            body.SolidVertices,seed.MemberKey,null,out var state,out var reason),reason);
        var reopened = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(state))!;
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(reopened, current, out var complete));
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(complete));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(reopened,
            current with { Start = new(current.Start.X - 100,current.Start.Y - 300,0) },
            out var physical,out var updated,out reason,useIndependentHorizontalFrame:true),reason);
        Assert.NotNull(physical); Assert.Equal(2, updated!.Version); Assert.NotNull(updated.SectionFrame);
    }

    [Fact]
    public void Working2Af9Equivalent_V2CompletionDoesNotChangeStateOrPhysicalFrame()
    {
        var seed = Seed();
        var (_, accepted, _, _, _) = Package(seed, seed.AcceptedPlanAxis);
        var before = JsonSerializer.Serialize(accepted);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(accepted, accepted.AcceptedPlanAxis, out var complete));
        Assert.Equal(before, JsonSerializer.Serialize(complete));
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(accepted,
            accepted.AcceptedPlanAxis with { End = new(1,2,0) }, out _));
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("STRETCH")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData("COPY")]
    [InlineData("MIRROR_NO")]
    [InlineData("MIRROR_YES")]
    public void CreationGeometryCompletesIntoOneValidV2MemberModel(string command)
    {
        var seed = Seed();
        var (_, state, _, _, _) = Package(seed, seed.AcceptedPlanAxis);
        var plan = state.AcceptedPlanAxis;
        RoofOrdinaryPhysicalBuildState? prepared = state;
        if (command is "MOVE" or "COPY")
        {
            plan = new(new(plan.Start.X + 1500,plan.Start.Y - 500,0), new(plan.End.X + 1500,plan.End.Y - 500,0));
            Assert.True(command == "MOVE" ? RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, plan,out prepared) :
                RoofOrdinaryCopyCloneRules.TryPreparePhysicalCopy(state,plan,out prepared));
        }
        else if (command.StartsWith("MIRROR",StringComparison.Ordinal))
        {
            plan = new(new(90000-plan.Start.X,plan.Start.Y,0),new(90000-plan.End.X,plan.End.Y,0));
            Assert.True(RoofOrdinaryMirrorRules.TryPreparePhysicalMirror(state,plan,out prepared));
        }
        else
        {
            var fraction = command == "EXTEND" ? -0.1 : command is "BREAK" or "BREAKATPOINT" ? 0.6 : 0.2;
            plan = plan with { Start = new(plan.Start.X + fraction*(plan.End.X-plan.Start.X),
                plan.Start.Y + fraction*(plan.End.Y-plan.Start.Y),0) };
        }
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(prepared!,plan,out _,out var updated,out var reason,
            useIndependentHorizontalFrame:true),reason);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryComplete(updated!,plan,out var persisted));
        Assert.Equal(2,persisted!.Version); Assert.Equal(plan,persisted.AcceptedPlanAxis);
        Assert.Equal(80,persisted.WidthMm); Assert.Equal(160,persisted.HeightMm); Assert.NotNull(persisted.SectionFrame);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(persisted));
    }

    [Fact]
    public void MissingPrerequisitesAndMismatchedGeometryFailClosed()
    {
        var seed = Seed();
        var (body, _, frame, top, upper) = Package(seed, seed.AcceptedPlanAxis);
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(seed.AcceptedPlanAxis,0,160,
            frame,upper,top,body.SolidVertices,seed.MemberKey,null,out _,out _));
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(seed.AcceptedPlanAxis,80,160,
            frame,upper,top,body.SolidVertices.Select(p => new RoofPoint3D(p.X,p.Y,p.Z + 1)).ToArray(),
            seed.MemberKey,null,out _,out var reason));
        Assert.Equal("CurrentPhysicalCutsOrPlanPlacementCannotBeReproduced",reason);
        Assert.False(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(seed.AcceptedPlanAxis,80,160,
            frame with { LongitudinalAxis = new(0,0,1) },upper,top,body.SolidVertices,seed.MemberKey,null,out _,out _));
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(seed,
            seed.AcceptedPlanAxis with { Start = new(seed.AcceptedPlanAxis.Start.X + 606,seed.AcceptedPlanAxis.Start.Y,0) },out _));
    }

    private static (RoofAutomaticRafterPhysicalMember Body, RoofOrdinaryPhysicalBuildState State,
        RoofOrdinarySectionFrame Frame, RoofPoint3D[] Top, RoofPoint3D Upper) Package(
        RoofOrdinaryPhysicalBuildState seed, RoofSegment3D plan)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(seed,plan,out var body,out var state,out var reason,
            useIndependentHorizontalFrame:true),reason);
        Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(state!,body!,out var frame));
        var section = state!.SectionFrame!;
        var upper = frame!.UpperAxis.Start;
        var top = body!.SolidVertices.Where(p => Math.Abs(Dot(new(p.X-upper.X,p.Y-upper.Y,p.Z-upper.Z),
            section.HeightAxis)) < 1e-6).ToArray();
        return (body,state,section,top,upper);
    }

    private static RoofOrdinaryPhysicalBuildState Host2942State()
    {
        var seed = Seed();
        // HOST logs: current Plan 2942, original member eave plane at 3000,
        // current live owner shifted elsewhere. Face0/station8 terminates at Hip.
        var dx = 40559.501492803 - seed.Nodes[0].X;
        var dy = 10609.41891628256 - seed.Nodes[0].Y;
        var nodes = seed.Nodes.Select(p => new RoofPoint3D(p.X+dx,p.Y+dy,p.Z)).ToArray();
        var current = new RoofSegment3D(new(47809.501492803,10609.41891628256,0),
            new(47809.501492803,13359.41891628256,0));
        var sources = seed.Edges.Select((edge,index) => (edge,index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                new(nodes[item.edge.Start],nodes[item.edge.End]),120)).ToArray();
        return seed with { Nodes=nodes,AcceptedPlanAxis=current,StructuralSources=sources,
            MemberKey=new(RoofGeneratedTimberKind.Rafter,RafterRoofFace.Face0,8),
            Anchor=seed.Anchor with { PlanStart=new(current.Start.X,current.Start.Y),
                PlanEnd=new(current.End.X,current.End.Y),PlanLengthMm=2750 } };
    }

    private static RoofOrdinaryPhysicalBuildState Seed()
    {
        const double x = 36141.02099635819, y = 11478.696868240506;
        var footprint = RoofFootprintValidator.Validate(new([new(x,y),new(x+10000,y),new(x+10000,y+6000),new(x,y+6000)],true)).Footprint!;
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint,new(45),RoofKind.Hip)).Geometry);
        var anchor = RoofFaceRafterLayoutService.Create(roof.Topology,900).Layout!.Segments.First(s =>
            s.SourceFaceIndex == 0 && s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return RoofOrdinaryPhysicalBuildStateRules.Capture(roof.Topology,anchor,
            new(RoofGeneratedTimberKind.Rafter,RafterRoofFace.Face0,5),3000,80,160,new(),
            Array.Empty<RoofStructuralRafterTrimSource>(),new(new(41591.02099635819,y,0),new(41591.02099635819,y+3000,0)));
    }
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
}
