using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Portable native-result validation and shared physical behavior; adapter guards are not HOST proof.</summary>
public sealed class RoofOrdinaryLengthenLifecycleTests
{
    [Theory]
    [InlineData("LENGTHEN")] [InlineData("_LENGTHEN")] [InlineData("_.lengthen")] [InlineData(" 'LENGTHEN ")]
    public void CommandRoutesThroughExistingSnapshotFirstClaimAndUndoScope(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsLengthenCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsOrdinaryPlanGeometryEditCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.True(LiveGeometryCommandRules.RequiresGroupedUndoMark(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsLengthenCommand("GRIP_LENGTHEN"));
    }

    // These are supplied final native geometries, not implementations of the mode prompts.
    [Theory]
    [InlineData("Delta positive", true, -0.2)] [InlineData("Delta positive", false, 1.2)]
    [InlineData("Delta negative", true, 0.2)] [InlineData("Delta negative", false, 0.8)]
    [InlineData("Total", false, 1.4)] [InlineData("Percent", false, 0.75)]
    [InlineData("Dynamic", true, -0.15)] [InlineData("Dynamic", false, 1.15)]
    public void NativeModeResults_KeepFixedEndpointAndRebuildExactAxis(string mode, bool start, double fraction)
    {
        Assert.NotEmpty(mode);
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        var before = state.AcceptedPlanAxis;
        var final = Edit(before, start, fraction);
        Assert.True(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(before, final, out var reason), reason);
        Assert.Equal(start ? RoofOrdinaryGripChange.Start : RoofOrdinaryGripChange.End,
            RoofOrdinaryLengthenRules.Endpoint(before, final));
        Assert.Equal(start ? before.End : before.Start, start ? final.End : final.Start);
        var (body, updated) = Build(state, final);
        Assert.Equal(final, body.PlanAxis);
        Assert.Equal(final, updated.AcceptedPlanAxis);
        Assert.Equal(80, body.WidthMm); Assert.Equal(160, body.HeightMm);
        var frame = body.SectionOrientation!.NewFrame;
        Assert.InRange(Math.Abs(frame.WidthAxis.Z), 0, 1e-9);
        Assert.True(frame.HeightAxis.Z > 0);
        Assert.InRange(Dot(Cross(frame.LongitudinalAxis, frame.WidthAxis), frame.HeightAxis), 1-1e-9, 1+1e-9);
        var prism = body.HorizontalCut?.SourcePrismVertices ?? body.StructuralCut?.SourcePrismVertices ??
            body.RidgeOverlapCut?.SourcePrismVertices ?? body.RidgeMeetCut?.SourcePrismVertices ?? body.SolidVertices;
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80-1e-7, 80+1e-7);
        for (var i = 0; i < 4; i++)
            Assert.InRange(Dot(Sub(prism[i], prism[i+4]), frame.HeightAxis), 160-1e-7, 160+1e-7);
        var direction = Sub(final.End, final.Start);
        Assert.InRange(Math.Abs(frame.LongitudinalAxis.X*direction.Y-frame.LongitudinalAxis.Y*direction.X), 0, 1e-5);
        Assert.True(frame.LongitudinalAxis.X*direction.X+frame.LongitudinalAxis.Y*direction.Y > 0);
        Assert.Equal(before, state.AcceptedPlanAxis);
        Assert.NotEqual(before.Start.DistanceTo(before.End), body.PlanAxis.Start.DistanceTo(body.PlanAxis.End));
    }

    [Fact]
    public void LengthenedOutsideOriginalBoundary_UsesSharedPlaneAndFreeEndWithoutAutoSlot()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var final = Edit(state.AcceptedPlanAxis, true, -0.2);
        var (body, updated) = Build(state, final);
        Assert.Equal(RoofRafterBoundaryRole.Free, updated.Anchor.StartBoundaryRole);
        Assert.Equal(RoofRafterBoundaryRole.Ridge, updated.Anchor.EndBoundaryRole);
        Assert.Equal(state.MemberKey, updated.MemberKey);
        Assert.Equal(final, body.PlanAxis);
        Assert.True(body.PhysicalLengthMm > state.AcceptedPlanAxis.Start.DistanceTo(state.AcceptedPlanAxis.End));
    }

    [Fact]
    public void RepeatedIndependentLengthEdits_PersistV2AfterRoundtripAndKeepFrame_TranslationRebaseRemainsStrict()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        var (first, saved) = Build(state, state.AcceptedPlanAxis);
        var frame = first.SectionOrientation!.NewFrame;
        foreach (var (start, fraction) in new[] { (true, -0.1), (false, 0.8), (false, 1.2), (true, 0.1) })
        {
            var final = Edit(saved.AcceptedPlanAxis, start, fraction);
            Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(saved, final, out _));
            var (_, next) = Build(saved, final);
            saved = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(next))!;
            Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(saved));
            Assert.Equal(2, saved.Version);
            Assert.Equal(final, saved.AcceptedPlanAxis);
            Assert.InRange(frame.WidthAxis.DistanceTo(saved.SectionFrame!.WidthAxis), 0, 1e-8);
            Assert.InRange(frame.HeightAxis.DistanceTo(saved.SectionFrame.HeightAxis), 0, 1e-8);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void BothEndpointsYawTranslationInversionDegeneracyAndNaN_AreRejected(int kind)
    {
        var before = new RoofSegment3D(new(0,0,0), new(0,3000,0));
        var final = kind switch
        {
            0 => new RoofSegment3D(new(0,-100,0), new(0,3100,0)),
            1 => before with { End = new(500,3000,0) },
            2 => new RoofSegment3D(new(100,0,0), new(100,3000,0)),
            3 => before with { End = new(0,-100,0) },
            4 => before with { End = before.Start },
            _ => before with { End = new(double.NaN,3000,0) },
        };
        Assert.False(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(before, final, out var reason));
        Assert.NotEqual("none", reason);
    }

    [Fact]
    public void QueryOnlyRejectedNativeAndZNoise_DoNotBecomeLengthEdit()
    {
        var before = new RoofSegment3D(new(0,0,0),new(0,3000,0));
        Assert.False(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(before,before,out _));
        Assert.Equal(RoofOrdinaryGripChange.None, RoofOrdinaryLengthenRules.Endpoint(before,
            before with { Start = before.Start with { Z = 100 }, End = before.End with { Z = 100 } }));
        var final = Edit(before,false,1.2);
        Assert.Equal(final, RoofOrdinaryGripLifecycleRules.Plan(final with
        { Start = final.Start with { Z = 100 }, End = final.End with { Z = 100 } }));
    }

    [Theory]
    [InlineData(true,true,true)] [InlineData(true,false,false)] [InlineData(false,false,true)]
    public void AutoSetHasOneSharedDecision_IndependentNeverPrompts(bool enabled, bool yes, bool accepted)
    {
        var calls = 0;
        var result = WarningPreferenceRules.ConfirmDetach(new() { ConfirmAutomaticMemberDetach = enabled },true,
            () => { calls++; return new(yes,false); });
        Assert.Equal(accepted,result.Accepted);
        Assert.Equal(enabled ? 1 : 0,calls);
        Assert.Equal(!enabled,result.AutomaticConfirm);
        Assert.True(WarningPreferenceRules.ConfirmDetach(new(),false,
            () => throw new InvalidOperationException("Independent must not prompt")).Accepted);
    }

    [Fact]
    public void ElementIdUsesCurrentCuttingSignature_StableForSameRoundedLengthAndSplitsForChangedLength()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        TimberElementMeasurement Measure(RoofSegment3D plan)
        {
            var (body, _) = Build(state, plan);
            return TimberCalculator.Measure(new() { ElementId="K4",WidthMm=80,HeightMm=160,
                SlopeDegrees=Math.Acos(plan.Start.DistanceTo(plan.End)/body.PhysicalLengthMm)*180/Math.PI },
                plan.Start.DistanceTo(plan.End),50);
        }
        var before = Measure(state.AcceptedPlanAxis);
        var unchanged = Measure(Edit(state.AcceptedPlanAxis,false,1.0000001));
        Assert.Equal(TimberElementSignature.FromMeasurement(before),TimberElementSignature.FromMeasurement(unchanged));
        var same = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[]
        { new TimberElementItemNumberingCandidate(unchanged,true),new(before,false) });
        Assert.All(same, x => Assert.Equal("K4",x.ElementId));
        var changed = Measure(Edit(state.AcceptedPlanAxis,false,0.8));
        Assert.NotEqual(TimberElementSignature.FromMeasurement(before),TimberElementSignature.FromMeasurement(changed));
        var split = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[]
        { new TimberElementItemNumberingCandidate(changed,true),new(before,false) });
        Assert.NotEqual("K4",split[0].ElementId); Assert.Equal("K4",split[1].ElementId);
        // A later edit matching this existing group reuses its designation.
        var merge = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[]
        { new TimberElementItemNumberingCandidate(changed,true),new(changed with { Data=changed.Data with { ElementId="K8" } },false) });
        Assert.All(merge,x => Assert.Equal("K8",x.ElementId));
    }

    [Fact]
    public void AdapterUsesCommonAcceptancePersistenceMigrationIdentityAndAtomicNo_NoModeMathOrSolidOnlyBranch()
    {
        var lengthen = Read("RoofOrdinaryLengthenLifecycleService.cs");
        var shared = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Equal(1,lengthen.Split("MemberWarningPreferenceService.ConfirmAutomaticDetach()").Length-1);
        Assert.Contains("plans.Where(item => !item.Member.Independent)",lengthen);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Accept",lengthen);
        Assert.Contains("RoofOrdinaryGripLifecycleService.RecalculateDesignations",lengthen);
        Assert.Contains("RoofOrdinaryRotateLifecycleService.VerifyPackages",lengthen);
        Assert.Contains("RestoreAll(transaction, snapshot, affected)",lengthen);
        Assert.Contains("RestoreAll(rollback, snapshot, affected)",lengthen);
        Assert.True(lengthen.IndexOf("RecalculateDesignations",StringComparison.Ordinal) <
            lengthen.IndexOf("transaction.Commit()",StringComparison.Ordinal));
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist",shared);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate",shared);
        Assert.Contains("RoofIndependentOrdinaryDetachService.TryDetach",shared);
        Assert.Contains("Read(item.Member.PlanCopy)?.IndependentMemberId",shared);
        Assert.Contains("if (change == RoofOrdinaryGripChange.None)",lengthen);
        Assert.Contains("snapshot.ClaimedIds.UnionWith(member.Entities",lengthen);
        Assert.Contains("snapshot.ClaimedIds.UnionWith(item.Member.Entities",lengthen);
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds",lengthen);
        Assert.True(live.IndexOf("RoofOrdinaryGripLifecycleService.Process(",StringComparison.Ordinal) <
            live.IndexOf("RoofLiveResizeService.Process(",StringComparison.Ordinal));
        Assert.DoesNotContain("MaterializeOrdinaryMember",lengthen);
        Assert.DoesNotContain("RoofPhysical3DWarningService",lengthen);
        Assert.DoesNotContain("Guid.NewGuid",lengthen);
        foreach (var forbidden in new[] { "AlongMm","LateralMm","RotationRadians","StartOffsetMm","EndOffsetMm",
            "PhysicalReferenceSegment","HasGeometryOverride","TryWriteSuppressOverride","TryDetachAndPromote" })
            Assert.DoesNotContain(forbidden,lengthen);
    }

    [Fact]
    public void FreshContextAbortAndDispose_PreventStaleMembersAcrossCommands()
    {
        var lengthen = Read("RoofOrdinaryLengthenLifecycleService.cs");
        var shared = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("ROOF_ORDINARY_LENGTHEN_COMMAND_STATE",shared);
        Assert.Contains("LengthenAutoCount = LengthenIndependentCount = LengthenMixedCount = 0",shared);
        Assert.Contains("RoofOrdinaryLengthenLifecycleService.Cancel",live);
        var abort = lengthen[lengthen.IndexOf("internal static void Cancel",StringComparison.Ordinal)..
            lengthen.IndexOf("private static void RestoreAll",StringComparison.Ordinal)];
        Assert.Contains("RestoreAll(transaction, snapshot, affected)",abort);
        Assert.DoesNotContain("Accept(document",abort);
        Assert.Contains("snapshot.MarkProcessed",abort);
        Assert.Contains("_candidates.Clear()",shared);
        Assert.Contains("_processed.Clear()",shared);
        Assert.Contains("TraceCommandState(\"disposed\")",shared);
        foreach (var field in new[] { "endpoint=","collinear=","confirmationShown=","automaticConfirm=",
            "independentMemberIdBefore=","independentMemberIdAfter=","elementIdBefore=","elementIdAfter=",
            "buildStateSource=","suppressionWritten=False","manualOverrideWritten=False","attachedManualWritten=False" })
            Assert.Contains(field,lengthen);
    }

    [Fact]
    public void RebuildPlanDoesNotConsumeEditedIndependentGeometry_OriginalAutoSlotReturns()
    {
        var rebuild = Read("RoofGeneratedRafterSetService.cs");
        Assert.Contains("OrdinaryRafterRecipe",rebuild);
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore.Write",rebuild);
        var shared = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("Independent Ordinary GRIP remains in roof GROUP",shared);
        var rules = NeutralRead("RoofOrdinaryRebuildRules.cs");
        Assert.DoesNotContain("Independent", rules[(rules.IndexOf("CreateReplayPlan",StringComparison.Ordinal))..]);
        Assert.Contains("!item.Suppressed",rules);
    }

    private static RoofSegment3D Edit(RoofSegment3D axis, bool start, double fraction)
    {
        var point = new RoofPoint3D(axis.Start.X+fraction*(axis.End.X-axis.Start.X),
            axis.Start.Y+fraction*(axis.End.Y-axis.Start.Y),0);
        return start ? axis with { Start=point } : axis with { End=point };
    }
    private static (RoofAutomaticRafterPhysicalMember,RoofOrdinaryPhysicalBuildState) Build(
        RoofOrdinaryPhysicalBuildState state,RoofSegment3D plan)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state,plan,out var body,out var updated,out var reason,
            useIndependentHorizontalFrame:true),reason);
        return (body!,updated!);
    }
    private static RoofPoint3D Sub(RoofPoint3D a,RoofPoint3D b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    private static RoofPoint3D Cross(RoofPoint3D a,RoofPoint3D b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static double Dot(RoofPoint3D a,RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(),"src","AcKrovy.AutoCAD","Infrastructure",file));
    private static string NeutralRead(string file) => File.ReadAllText(Path.Combine(Root(),"src","AcKrovy.Core","Services","Roofs",file));
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName,"AcKrovy.sln"))) dir=dir.Parent;
        return dir!.FullName;
    }
}

