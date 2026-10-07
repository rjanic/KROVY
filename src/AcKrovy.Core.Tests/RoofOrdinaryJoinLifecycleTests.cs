using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryJoinLifecycleTests
{
    [Theory]
    [InlineData("JOIN")] [InlineData("_.join")] [InlineData(" 'JOIN ")]
    public void JoinHasNativeSnapshotAndUndoBoundary_ExcludesLegacyManualEdit(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsJoinCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(AcKrovy.Core.Services.LiveGeometryCommandRules.RequiresGroupedUndoMark(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
    }
    [Fact]
    public void JoinedOriginUsesExistingCodec_NoSourceIdentityReuseOrSchemaFork()
    {
        var identity=new RoofIndependentOrdinaryTimberData(1,Guid.NewGuid().ToString("N"),
            RoofIndependentOrdinaryOriginKind.Joined,RoofIndependentOrdinaryEntityRole.PlanLine,null,null);
        Assert.True(RoofIndependentOrdinaryTimberDataCodec.TryDecode(RoofIndependentOrdinaryTimberDataCodec.Encode(identity),out var decoded));
        Assert.Equal(identity,decoded); Assert.Equal(1,RoofIndependentOrdinaryTimberDataSchema.CurrentVersion);
    }
    [Theory]
    [InlineData(0.5,0.5,false)] [InlineData(0.65,0.35,false)]
    [InlineData(0.4,0.6,false)] [InlineData(0.5,0.5,true)]
    public void NativeTouchOverlapGapAndOpposite_ReuseOuterCutsAndPersistOneContinuousState(
        double firstEnd,double secondStart,bool reverse)
    {
        var whole = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35,27);
        var a = Piece(whole,0,firstEnd); var b = Piece(whole,reverse ? 1 : secondStart,reverse ? secondStart : 1);
        var sources = new[] { new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,a,"Smrek C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,b,"Smrek C24") };
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(sources,whole.AcceptedPlanAxis,out var joined,out var order,out var reason),reason);
        Assert.Equal(0,order!.StartOwner); Assert.Equal(1,order.EndOwner);
        Assert.Equal(firstEnd < secondStart ? 1 : 0,order.GapCount);
        Assert.Equal(firstEnd > secondStart ? 1 : 0,order.OverlapCount);
        var persisted = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(joined))!;
        var body = Build(persisted,persisted.AcceptedPlanAxis,out var updated);
        Assert.Equal(80,body.WidthMm); Assert.Equal(160,body.HeightMm);
        Assert.Equal(whole.AcceptedPlanAxis,body.PlanAxis);
        Assert.InRange(Math.Abs(updated.SectionFrame!.WidthAxis.Z),0,1e-10);
        Assert.True(updated.SectionFrame.HeightAxis.Z > 0);
        // Seam removal: the complete body matches the unbroken original, not
        // two joined source prisms with an internal cut left behind.
        var original = Build(whole,whole.AcceptedPlanAxis,out _);
        Assert.All(body.SolidVertices,p => Assert.Contains(original.SolidVertices,q => p.DistanceTo(q)<0.01));
        Assert.All(original.SolidVertices,p => Assert.Contains(body.SolidVertices,q => p.DistanceTo(q)<0.01));
        var shortened = new RoofSegment3D(updated.AcceptedPlanAxis.Start,At(updated.AcceptedPlanAxis,0.9));
        _ = Build(updated,shortened,out var lengthened);
        Assert.Equal(shortened,lengthened.AcceptedPlanAxis);
    }
    [Fact]
    public void ThreeBrokenPieces_OrderByProjection_IgnoreInputOrder()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var parts = new[] { Piece(state,0.66,1),Piece(state,0,0.33),Piece(state,0.33,0.66) };
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(parts.Select(s => new RoofOrdinaryJoinSource(s.AcceptedPlanAxis,s,"C24")).ToArray(),
            state.AcceptedPlanAxis,out var joined,out var order,out var reason),reason);
        Assert.Equal(1,order!.StartOwner); Assert.Equal(0,order.EndOwner);
        Assert.Equal(0,order.GapCount); Assert.Equal(0,order.OverlapCount);
        Assert.Equal(state.AcceptedPlanAxis,joined!.AcceptedPlanAxis);
    }
    [Fact]
    public void ContainedContributor_StillCombinesTwoSourcesWithoutChangingNativeExtents()
    {
        var whole=RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var contained=Piece(whole,0.25,0.75);
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(new[] { new RoofOrdinaryJoinSource(whole.AcceptedPlanAxis,whole,"C24"),
            new RoofOrdinaryJoinSource(contained.AcceptedPlanAxis,contained,"C24") },whole.AcceptedPlanAxis,out var joined,out var order,out var reason),reason);
        Assert.Equal(0,order!.StartOwner); Assert.Equal(0,order.EndOwner); Assert.Equal(1,order.OverlapCount);
        Assert.Equal(whole.AcceptedPlanAxis,joined!.AcceptedPlanAxis);
    }
    [Fact]
    public void ReversedNativeCarrierDirection_DoesNotChooseInputOrderOrInvertHeight()
    {
        var whole=RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var a=Piece(whole,0,0.5); var b=Piece(whole,0.5,1);
        var native=new RoofSegment3D(whole.AcceptedPlanAxis.End,whole.AcceptedPlanAxis.Start);
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(new[] {new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,a,"C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,b,"C24")},native,out var joined,out var order,out var reason),reason);
        Assert.Equal(1,order!.StartOwner); Assert.Equal(0,order.EndOwner);
        Assert.Equal(native,joined!.AcceptedPlanAxis);
        Assert.True(joined.SectionFrame!.HeightAxis.Z>0); Assert.Equal(0,joined.SectionFrame.WidthAxis.Z);
    }
    [Fact]
    public void OnlyOuterOwnersContributeCutModes_InternalInactiveSettingCannotBlockJoin()
    {
        var whole=RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var a=Piece(whole,0,0.5); var b=Piece(whole,0.5,1) with {Settings=new(LowerEndCutMode.Horizontal,RidgeJoinMode.Meet)};
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(new[] {new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,a,"C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,b,"C24")},whole.AcceptedPlanAxis,out var joined,out _,out var reason),reason);
        Assert.Equal(a.Settings.LowerEndCutMode,joined!.Settings.LowerEndCutMode);
        Assert.Equal(b.Settings.RidgeJoinMode,joined.Settings.RidgeJoinMode);
    }
    [Theory]
    [InlineData("width")] [InlineData("height")] [InlineData("material")] [InlineData("elevation")]
    [InlineData("frame")] [InlineData("missing")]
    public void IncompatibleSources_ProduceNoPreparedState(string defect)
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var a = Piece(state,0,0.5); var b = Piece(state,0.5,1);
        RoofOrdinaryPhysicalBuildState? invalid = defect switch {
            "width" => b with { WidthMm=100 }, "height" => b with { HeightMm=200 },
            "elevation" => b with { EaveElevationMm=b.EaveElevationMm+10 },
            "frame" => b with { SectionFrame=b.SectionFrame! with { WidthAxis=new(0,0,1) } },
            "missing" => null, _ => b };
        Assert.False(RoofOrdinaryJoinRules.TryPrepare(new[] { new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,a,"C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,invalid,defect=="material" ? "C16" : "C24") },
            state.AcceptedPlanAxis,out var result,out _,out _));
        Assert.Null(result);
    }
    [Theory]
    [InlineData(false,0)] [InlineData(true,0)] [InlineData(false,0.5)]
    public void BentClosedOrBulgedNativePolyline_IsRejected(bool closed,double bulge)
    {
        Assert.False(RoofOrdinaryJoinRules.IsStraightPolyline(new RoofPoint3D[] { new(0,0,0),new(1000,0,0),new(1000,1000,0) },
            new[] {bulge,0d,0d},closed,out _));
    }
    [Fact]
    public void StraightPolyline_RequiresMonotonicExtentsAndZeroZ()
    {
        Assert.True(RoofOrdinaryJoinRules.IsStraightPolyline(new RoofPoint3D[] { new(0,0,0),new(1000,0,0),new(2000,0,0) },
            new[] {0d,0d,0d},false,out var axis));
        Assert.Equal(new RoofSegment3D(new(0,0,0),new(2000,0,0)),axis);
        Assert.False(RoofOrdinaryJoinRules.IsStraightPolyline(new RoofPoint3D[] { new(0,0,0),new(3000,0,0),new(2000,0,0) },
            new[] {0d,0d,0d},false,out _));
        Assert.False(RoofOrdinaryJoinRules.IsStraightPolyline(new RoofPoint3D[] { new(0,0,0),new(2000,0,1) },new[] {0d,0d},false,out _));
    }
    [Fact]
    public void NativeExtentsAreAuthority_NoCustomGapBridgeOrBentAxis()
    {
        var sources = new RoofSegment3D[] { new(new(0,0,0),new(1000,0,0)),new(new(2000,0,0),new(3000,0,0)) };
        Assert.True(RoofOrdinaryJoinRules.TryOrder(sources,new(new(0,0,0),new(3000,0,0)),out var order,out _));
        Assert.Equal(1,order!.GapCount);
        Assert.False(RoofOrdinaryJoinRules.TryOrder(sources,sources[0],out _,out _));
        Assert.False(RoofOrdinaryJoinRules.TryOrder(new[] {sources[0],new RoofSegment3D(new(1000,0,0),new(1000,1000,0))},
            new(new(0,0,0),new(1000,1000,0)),out _,out _));
    }
    [Fact]
    public void LegacySourceUsesSharedStrictMigration_JoinedStateSurvivesFurtherEdits()
    {
        var whole=RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var a=Piece(whole,0,0.5); var b=Piece(whole,0.5,1);
        var body=Build(a,a.AcceptedPlanAxis,out _);
        var frame=a.SectionFrame!;
        Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(a,a.AcceptedPlanAxis,a.Anchor.SourceFaceIndex,out var roofFrame));
        var upper=roofFrame!.UpperAxis.Start;
        double Plane(RoofPoint3D p) => (p.X-upper.X)*frame.HeightAxis.X+(p.Y-upper.Y)*frame.HeightAxis.Y+(p.Z-upper.Z)*frame.HeightAxis.Z;
        var top=body.SolidVertices.Where(p => Math.Abs(Plane(p))<0.01).ToArray();
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(a.AcceptedPlanAxis,80,160,frame,upper,top,
            body.SolidVertices,a.MemberKey,null,out var migrated,out var migration),migration);
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(new[] {new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,migrated,"C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,b,"C24")},whole.AcceptedPlanAxis,out var joined,out _,out var reason),reason);
        var persisted=JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(joined))!;
        _=Build(persisted,new(persisted.AcceptedPlanAxis.Start,At(persisted.AcceptedPlanAxis,0.9)),out var edited);
        Assert.Equal(2,edited.Version); Assert.Equal(80,edited.WidthMm); Assert.Equal(160,edited.HeightMm);
    }
    [Fact]
    public void MeasuredStructuralLegacyOuterCut_IsRetainedAfterJoin_InternalSeamRemoved()
    {
        var seed=RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var dx=40559.501492803-seed.Nodes[0].X; var dy=10609.41891628256-seed.Nodes[0].Y;
        var nodes=seed.Nodes.Select(p => new RoofPoint3D(p.X+dx,p.Y+dy,p.Z)).ToArray();
        var plan=new RoofSegment3D(new(47809.501492803,10609.41891628256,0),new(47809.501492803,13359.41891628256,0));
        var structural=seed.Edges.Select((e,i) => (e,i)).Where(p => p.e.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(p => new RoofStructuralRafterTrimSource(p.i,p.e.Kind==RoofTopologyEdgeKind.Hip ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                new(nodes[p.e.Start],nodes[p.e.End]),120)).ToArray();
        var whole=seed with {Nodes=nodes,AcceptedPlanAxis=plan,StructuralSources=structural,
            Anchor=seed.Anchor with {PlanStart=new(plan.Start.X,plan.Start.Y),PlanEnd=new(plan.End.X,plan.End.Y),PlanLengthMm=2750}};
        var a=Piece(whole,0,0.4); var b=Piece(whole,0.4,1);
        var body=Build(b,b.AcceptedPlanAxis,out _);
        Assert.True(RoofOrdinaryPhysicalFrameRules.TryCreate(b,body,out var frame));
        var upper=frame!.UpperAxis.Start; var section=b.SectionFrame!;
        var top=body.SolidVertices.Where(p => Math.Abs((p.X-upper.X)*section.HeightAxis.X+
            (p.Y-upper.Y)*section.HeightAxis.Y+(p.Z-upper.Z)*section.HeightAxis.Z)<1e-6).ToArray();
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.TryRecover(b.AcceptedPlanAxis,80,160,section,upper,top,body.SolidVertices,
            b.MemberKey,null,out var recovered,out var migration),migration);
        Assert.Equal("MeasuredMemberStructuralCutsVerified",migration);
        Assert.True(RoofOrdinaryJoinRules.TryPrepare(new[] {new RoofOrdinaryJoinSource(a.AcceptedPlanAxis,a,"C24"),
            new RoofOrdinaryJoinSource(b.AcceptedPlanAxis,recovered,"C24")},plan,out var joined,out _,out var reason),reason);
        Assert.Single(joined!.StructuralSources);
        var result=Build(joined,plan,out _); var original=Build(whole,plan,out _);
        Assert.All(result.SolidVertices,p => Assert.Contains(original.SolidVertices,q => p.DistanceTo(q)<0.01));
        Assert.All(original.SolidVertices,p => Assert.Contains(result.SolidVertices,q => p.DistanceTo(q)<0.01));
    }
    [Fact]
    public void AdapterClaimsNativeGraphBeforeGenericCleanup_OneDecisionAndOneAtomicPackage()
    {
        var join=Read("RoofOrdinaryJoinLifecycleService.cs"); var live=Read("LiveGeometrySynchronizationService.cs");
        Assert.True(live.IndexOf("var joinClaimed = RoofOrdinaryJoinLifecycleService.Process",StringComparison.Ordinal)<
            live.IndexOf("var structuralClaimedIds = RoofStructuralNativeEditService.Process",StringComparison.Ordinal));
        Assert.Contains("context.Before.Keys.Where(context.Touched.Contains)",join);
        Assert.Contains("context.Foreign != 0 || sources.Length < 2",join);
        Assert.Contains("results.Length != 1",join);
        Assert.Contains("context.Auto > 0",join);
        Assert.Equal(1,join.Split("ConfirmAutomaticDetach()").Length-1);
        Assert.Contains("RestoreAll(document,transaction,context,raw,sources)",join);
        Assert.Contains("RestoreAll(document,rollback,context,raw,sources)",join);
        Assert.True(join.IndexOf("foreach (var source in sources)",StringComparison.Ordinal)<
            join.IndexOf("RecalculateDesignations",StringComparison.Ordinal));
        Assert.Contains("Guid.NewGuid().ToString(\"N\")",join);
        Assert.Contains("Accept(document,transaction,joined,plan)",join);
        Assert.Contains("VerifyConsumed",join); Assert.Contains("PreserveRecipe",join);
        Assert.Contains("RoofOrdinaryRotateLifecycleService.VerifyPackages",join);
        Assert.Contains("erasedSourceHandles = erasedSourceHandles.Where(handle => !handles.Contains(handle))",live);
        Assert.DoesNotContain("CreateBox",join); Assert.DoesNotContain("CreateExtrudedSolid",join);
        Assert.DoesNotContain("RoofManualOverride",join); Assert.DoesNotContain("Suppressed = true",join);
        Assert.DoesNotContain("AK_JOIN",Read("../Commands/AcKrovyCommands.cs"));
        foreach (var transient in new[] {"Before.Clear()","Groups.Clear()","Touched.Clear()","Appended.Clear()","Claimed.Clear()"}) Assert.Contains(transient,join);
        Assert.Contains("Trace(\"disposed\")",join);
        Assert.Contains("cancel: true",live);
    }
    private static string Read(string name)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"AcKrovy.sln"))) directory=directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName,"src","AcKrovy.AutoCAD","Infrastructure",name));
    }
    private static RoofOrdinaryPhysicalBuildState Piece(RoofOrdinaryPhysicalBuildState state,double start,double end)
    {
        _ = Build(state,new(At(state.AcceptedPlanAxis,start),At(state.AcceptedPlanAxis,end)),out var updated);
        return updated;
    }
    private static RoofAutomaticRafterPhysicalMember Build(RoofOrdinaryPhysicalBuildState state,RoofSegment3D plan,
        out RoofOrdinaryPhysicalBuildState updated)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state,plan,out var body,out var next,out var reason,true),reason);
        updated=next!; return body!;
    }
    private static RoofPoint3D At(RoofSegment3D s,double t) => new(s.Start.X+(s.End.X-s.Start.X)*t,s.Start.Y+(s.End.Y-s.Start.Y)*t,0);
}
