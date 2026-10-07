using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryRotateLifecycleTests
{
    [Theory]
    [InlineData(15)] [InlineData(37)] [InlineData(90)] [InlineData(-20)] [InlineData(180)]
    public void FinalNativeAxis_RebuildsWithExactSectionAndHorizontalRightHandedFrame(double degrees)
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        var originalJson = JsonSerializer.Serialize(state);
        var center = state.AcceptedPlanAxis.Start;
        var rotated = Rotate(state.AcceptedPlanAxis, center, degrees);
        // Nonzero native Z is presentation residue; accepted Plan is always Z=0.
        var native = new RoofSegment3D(rotated.Start with { Z = 550 }, rotated.End with { Z = 550 });
        var plan = RoofOrdinaryGripLifecycleRules.Plan(native);
        Assert.Equal(rotated, plan);
        Assert.True(RoofOrdinaryRotateRules.IsRigidPlanRotation(state.AcceptedPlanAxis, native));
        Assert.True(RoofOrdinaryRotateRules.RotationDetected(state.AcceptedPlanAxis, native));
        var (body, updated) = Build(state, plan);
        Assert.Equal(plan, body.PlanAxis);
        Assert.Equal(plan, updated.AcceptedPlanAxis);
        Assert.Equal(2, updated.Version);
        Assert.Equal(80, body.WidthMm); Assert.Equal(160, body.HeightMm);
        var frame = body.SectionOrientation!.NewFrame;
        Assert.Equal(0, frame.WidthAxis.Z);
        Assert.True(frame.HeightAxis.Z > 0);
        Assert.InRange(Dot(Cross(frame.LongitudinalAxis, frame.WidthAxis), frame.HeightAxis), 1 - 1e-9, 1 + 1e-9);
        var prism = body.HorizontalCut?.SourcePrismVertices ?? body.StructuralCut?.SourcePrismVertices ??
            body.RidgeOverlapCut?.SourcePrismVertices ?? body.RidgeMeetCut?.SourcePrismVertices ?? body.SolidVertices;
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80 - 1e-7, 80 + 1e-7);
        for (var i = 0; i < 4; i++)
            Assert.InRange(Dot(Sub(prism[i], prism[i + 4]), frame.HeightAxis), 160 - 1e-7, 160 + 1e-7);
        // Actual upper-axis projection agrees with the final directed Plan, including negative angles.
        var longitudinal = frame.LongitudinalAxis;
        Assert.InRange(Math.Abs(longitudinal.X * (plan.End.Y - plan.Start.Y) -
            longitudinal.Y * (plan.End.X - plan.Start.X)), 0, 1e-5);
        Assert.True(longitudinal.X * (plan.End.X - plan.Start.X) +
            longitudinal.Y * (plan.End.Y - plan.Start.Y) > 0);
        Assert.Equal(originalJson, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void RepeatedIndependentRotations_UseOwnUpdatedV2StateAcrossRoundtrip_NoAutoInventory()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        foreach (var degrees in new[] { 15d, 37d, -20d, 90d, -90d })
        {
            var final = Rotate(state.AcceptedPlanAxis, state.AcceptedPlanAxis.Start, degrees);
            var (_, next) = Build(state, final);
            state = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(JsonSerializer.Serialize(next))!;
            Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(state));
            Assert.Equal(final, state.AcceptedPlanAxis);
            Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, final, out var rebased));
            Assert.Equal(final, rebased!.AcceptedPlanAxis);
            Assert.Equal(0, state.SectionFrame!.WidthAxis.Z);
        }
    }

    [Fact]
    public void RotationDoesNotWeakenRigidTranslationRebase_AndRejectsScaleAndInvalidInput()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture();
        var final = Rotate(state.AcceptedPlanAxis, state.AcceptedPlanAxis.Start, 37);
        Assert.False(RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, final, out _));
        Assert.False(RoofOrdinaryRotateRules.IsRigidPlanRotation(state.AcceptedPlanAxis,
            final with { End = new(final.End.X + 200, final.End.Y, 0) }));
        Assert.False(RoofOrdinaryRotateRules.IsRigidPlanRotation(state.AcceptedPlanAxis,
            final with { End = new(double.NaN, 0, 0) }));
        Assert.False(RoofOrdinaryRotateRules.RotationDetected(state.AcceptedPlanAxis, state.AcceptedPlanAxis));
    }

    [Theory]
    [InlineData(15)] [InlineData(37)] [InlineData(90)] [InlineData(-20)]
    public void OffAxisNativeBasePoint_IsAcceptedWithoutGuessingBasePoint(double degrees)
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        var basePoint = new RoofPoint3D(state.AcceptedPlanAxis.Start.X - 350, state.AcceptedPlanAxis.Start.Y + 125, 0);
        var final = Rotate(state.AcceptedPlanAxis, basePoint, degrees);
        Assert.Equal(RoofOrdinaryGripChange.Both, RoofOrdinaryGripLifecycleRules.Classify(state.AcceptedPlanAxis, final));
        Assert.True(RoofOrdinaryRotateRules.IsRigidPlanRotation(state.AcceptedPlanAxis, final));
        var (body, updated) = Build(state, final);
        Assert.Equal(final, body.PlanAxis);
        Assert.Equal(final, updated.AcceptedPlanAxis);
    }

    [Theory]
    [InlineData(true, true, true)] [InlineData(true, false, false)] [InlineData(false, true, true)]
    public void MultiAutoAndIndependent_UsesOnePreferenceDecision(bool warning, bool yes, bool accepted)
    {
        var calls = 0;
        var decision = WarningPreferenceRules.ConfirmDetach(new() { ConfirmAutomaticMemberDetach = warning },
            hasAutomaticMember: true, () => { calls++; return new(yes, false); });
        Assert.Equal(accepted, decision.Accepted);
        Assert.Equal(warning ? 1 : 0, calls);
        Assert.Equal(!warning, decision.AutomaticConfirm);
        var independent = WarningPreferenceRules.ConfirmDetach(new(), false,
            () => throw new InvalidOperationException("Independent must not prompt"));
        Assert.True(independent.Accepted); Assert.False(independent.ConfirmationShown);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PhysicalOnlyWarningPreference_NeverAcceptsDerivedRotate(bool warning)
    {
        var calls = 0;
        var decision = WarningPreferenceRules.RejectDerived3DEdit(new() { WarnDerived3DEdit = warning },
            () => { calls++; return false; });
        Assert.False(decision.Accepted);
        Assert.Equal(warning ? 1 : 0, calls);
    }

    [Fact]
    public void RotationWithSameManufacturingSignature_KeepsElementId_ChangedSignatureSplits()
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 0);
        // +20 and -20 about the middle have equal 3D length on the same member plane.
        var center = new RoofPoint3D(state.AcceptedPlanAxis.Start.X,
            (state.AcceptedPlanAxis.Start.Y + state.AcceptedPlanAxis.End.Y) / 2, 0);
        var plus = Rotate(state.AcceptedPlanAxis, center, 20);
        var minus = Rotate(state.AcceptedPlanAxis, center, -20);
        var (first, saved) = Build(state, plus);
        var (second, _) = Build(saved, minus);
        TimberElementMeasurement Measure(RoofAutomaticRafterPhysicalMember body) => TimberCalculator.Measure(new()
        { ElementId = "K4", SlopeDegrees = Math.Acos(body.PlanAxis.Start.DistanceTo(body.PlanAxis.End) /
            body.PhysicalLengthMm) * 180 / Math.PI }, body.PlanAxis.Start.DistanceTo(body.PlanAxis.End), 50);
        var a = Measure(first); var b = Measure(second);
        Assert.Equal(TimberElementSignature.FromMeasurement(a), TimberElementSignature.FromMeasurement(b));
        var assigned = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[]
        { new TimberElementItemNumberingCandidate(b, false), new(a, false) });
        Assert.All(assigned, item => Assert.Equal("K4", item.ElementId));
        var (horizontal, _) = Build(saved, Rotate(state.AcceptedPlanAxis, center, 90));
        var c = Measure(horizontal);
        Assert.NotEqual(TimberElementSignature.FromMeasurement(a), TimberElementSignature.FromMeasurement(c));
        assigned = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[]
        { new TimberElementItemNumberingCandidate(c, true), new(a, false) });
        Assert.NotEqual("K4", assigned[0].ElementId); Assert.Equal("K4", assigned[1].ElementId);
    }

    [Fact]
    public void Adapter_ClaimsBeforeLegacyAndUsesOneAtomicTransactionAndCommonPersistence()
    {
        var engine = Read("RoofOrdinaryRotateLifecycleService.cs");
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("IsRotateCommand(command)", grip);
        Assert.Contains("RoofOrdinaryRotateLifecycleService.Process", grip);
        Assert.True(live.IndexOf("RoofOrdinaryGripLifecycleService.Process(", StringComparison.Ordinal) <
            live.IndexOf("RoofLiveResizeService.Process(", StringComparison.Ordinal));
        Assert.Equal(1, engine.Split("MemberWarningPreferenceService.ConfirmAutomaticDetach()").Length - 1);
        var accept = engine.IndexOf("RoofOrdinaryGripLifecycleService.Accept(", StringComparison.Ordinal);
        var recalc = engine.IndexOf("RoofOrdinaryGripLifecycleService.RecalculateDesignations", StringComparison.Ordinal);
        var commit = engine.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(accept < recalc && recalc < commit);
        Assert.Contains("RestoreAll(transaction, snapshot, affected)", engine);
        Assert.Contains("RestoreAll(rollback, snapshot, affected)", engine);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Verify", engine);
        Assert.Contains("VerifyPackages", engine);
        Assert.Contains("RoofOrdinaryGripLifecycleService.RestoreGroup", engine);
        Assert.Contains("RoofPhysical3DWarningService.Show()", engine);
        Assert.Contains("if (snapshot.ProcessingStarted) return snapshot.ClaimedIds", engine);
        Assert.Contains("refreshAllTimberAnnotations = false", live);
        Assert.Contains("state.AcceptedPlanAxis != Axis(line)", engine);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.Persist", grip);
        Assert.DoesNotContain("RoofOrdinaryPhysicalBuildStateStore.Write", engine);
        Assert.DoesNotContain("Guid.NewGuid", engine);
        Assert.DoesNotContain("TransformBy", engine);
        Assert.Contains("RoofIndependentOrdinaryPhysicalStateService.TryMigrate", grip);
        Assert.Contains("if (!member.Independent && sourceChangedOwners.Contains(member.OwnerId)) continue", engine);
        Assert.Contains("!id.IsNull && !sourceChangedOwners.Contains(id)", engine);
    }

    [Fact]
    public void Adapter_MixedPlanWinsAndPerCommandCountsAreDisposed_NoLegacyState()
    {
        var engine = Read("RoofOrdinaryRotateLifecycleService.cs");
        Assert.Contains("item.Change == RoofOrdinaryGripChange.None", engine);
        Assert.Contains("if (change != RoofOrdinaryGripChange.None && solidChanged)", engine);
        Assert.Contains("IndependentMemberId", engine);
        Assert.Contains("RoofCommandLifecycleTerminalState.MarkHandled", engine);
        foreach (var field in new[] { "rotationDetected=", "confirmationShown=", "automaticConfirm=",
            "independentMemberIdBefore=", "independentMemberIdAfter=", "elementIdBefore=", "elementIdAfter=",
            "buildStateSource=", "suppressionWritten=False", "manualOverrideWritten=False", "attachedManualWritten=False" })
            Assert.Contains(field, engine);
        foreach (var forbidden in new[] { "AlongMm", "LateralMm", "RotationRadians", "StartOffsetMm", "EndOffsetMm",
            "PhysicalReferenceSegment", "HasGeometryOverride", "TryWriteSuppressOverride", "TryDetachAndPromote" })
            Assert.DoesNotContain(forbidden, engine);
        var grip = Read("RoofOrdinaryGripLifecycleService.cs");
        Assert.Contains("ROOF_ORDINARY_ROTATE_COMMAND_STATE", grip);
        Assert.Contains("RotateAutoCount = RotateIndependentCount = RotatePhysicalOnlyCount = RotateMixedCount = 0", grip);
        Assert.Contains("TraceCommandState(\"disposed\")", grip);
        Assert.Contains("Independent Ordinary GRIP remains in roof GROUP", grip);
        var rebuild = Read("RoofGeneratedRafterSetService.cs");
        Assert.Contains("OrdinaryRafterRecipe", rebuild);
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore.Write", rebuild);
    }

    private static (RoofAutomaticRafterPhysicalMember, RoofOrdinaryPhysicalBuildState) Build(
        RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan)
    {
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, plan,
            out var body, out var updated, out var reason, useIndependentHorizontalFrame: true), reason);
        return (body!, updated!);
    }
    private static RoofSegment3D Rotate(RoofSegment3D source, RoofPoint3D center, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        RoofPoint3D Map(RoofPoint3D p) => new(center.X + (p.X - center.X) * Math.Cos(angle) -
            (p.Y - center.Y) * Math.Sin(angle), center.Y + (p.X - center.X) * Math.Sin(angle) +
            (p.Y - center.Y) * Math.Cos(angle), 0);
        return new(Map(source.Start), Map(source.End));
    }
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
