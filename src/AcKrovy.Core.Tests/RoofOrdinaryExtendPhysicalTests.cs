using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Shared routing, semantic geometry and physical planner regressions.
/// Actual AutoCAD Solid3d writes/callback ordering require the separate HOST retest.</summary>
public sealed class RoofOrdinaryExtendPhysicalTests
{
    [Theory]
    [InlineData("MOVE")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("_.extend")]
    [InlineData("'EXTEND")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    public void AcceptedMemberCommand_EntersCommonPhysicalReconcile(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(command));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedExtend_ComposesEndpointOverride_ReplacesOnlyTargetBody_WithSameKey(bool extendStart)
    {
        var f = Solve();
        var canonical = new RoofGeneratedMemberGeometry(f.Axis.Start, f.Axis.End);
        var shortened = extendStart ? new RoofSegment3D(At(f.Axis, 0.2), f.Axis.End) : new(f.Axis.Start, At(f.Axis, 0.7));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(canonical,
            new(shortened.Start, shortened.End), new(0, 0, 1), f.Key, "R1", out var initial));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            new(shortened.Start, shortened.End), canonical, new(0, 0, 1), out var startDelta, out var endDelta, out _, out _));
        var accepted = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(initial, f.Key, "R1", startDelta, endDelta);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, new(0, 0, 1), accepted, out var replay));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(canonical, replay));
        var before = Build(f, initial);
        var after = Build(f, accepted);
        var old = before.Members.Single(member => member.MemberKey == f.Key);
        var target = after.Members.Single(member => member.MemberKey == f.Key);
        Assert.Equal(old.MemberKey, target.MemberKey);
        Assert.Equal(old.PhysicalIdentity, target.PhysicalIdentity);
        Assert.NotEqual(old.PlanAxis, target.PlanAxis);
        Assert.Equal(f.Axis, target.PlanAxis);
        Assert.Equal(0, target.PlanAxis.Start.Z);
        Assert.Equal(0, target.PlanAxis.End.Z);
        Assert.True(target.PhysicalLengthMm > old.PhysicalLengthMm);
        Assert.NotEqual(old.SolidVertices, target.SolidVertices);
        AssertTargetReplacement(before, after, target.PhysicalIdentity);
        foreach (var unrelated in before.Members.Where(member => member.MemberKey != f.Key))
        {
            var current = after.Members.Single(member => member.MemberKey == unrelated.MemberKey);
            Assert.Equal(unrelated.PlanAxis, current.PlanAxis);
            Assert.Equal(unrelated.SolidVertices, current.SolidVertices);
        }
    }

    [Theory]
    [InlineData(RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofAttachedManualOrigin.Split)]
    public void AttachedExtend_PreservesUuid_ReplacesOnlyOwnBody(RoofAttachedManualOrigin origin)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("EXTEND"));
        var f = Solve();
        var initialAxis = new RoofSegment3D(At(f.Axis, 0.25), At(f.Axis, 0.7));
        var finalAxis = new RoofSegment3D(initialAxis.Start, At(f.Axis, 0.9));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(f.Axis.Start, f.Axis.End,
            initialAxis.Start, initialAxis.End, out var initialRelative));
        var initial = new RoofAttachedManualTimberData(4, "AB", "CHILD", RoofTimberChildRole.AttachedManual,
            f.Key, initialRelative, origin, RoofAttachedManualIdentityRules.Create());
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(f.Axis.Start, f.Axis.End,
            finalAxis.Start, finalAxis.End, out var finalRelative));
        var accepted = initial with { RelativeSegment = finalRelative };
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(f.Axis.Start, f.Axis.End, accepted.RelativeSegment!,
            out var replayStart, out var replayEnd));
        Assert.True(replayStart.DistanceTo(finalAxis.Start) < 1e-5);
        Assert.True(replayEnd.DistanceTo(finalAxis.End) < 1e-5);
        Assert.Equal(initial.SemanticIdentity, accepted.SemanticIdentity);
        var before = Append(f, initial, initialAxis);
        var after = Append(f, accepted, finalAxis);
        var key = RoofAttachedManualIdentityRules.PhysicalKey(initial);
        Assert.Equal(key, RoofAttachedManualIdentityRules.PhysicalKey(accepted));
        Assert.Equal(finalAxis, after.Members.Single(member => member.PhysicalIdentity == key).PlanAxis);
        Assert.True(after.Members.Single(member => member.PhysicalIdentity == key).PhysicalLengthMm >
            before.Members.Single(member => member.PhysicalIdentity == key).PhysicalLengthMm);
        AssertTargetReplacement(before, after, key);
    }

    [Fact]
    public void ExtendNoOp_HasNoAcceptedChange_AndDoesNotRebuildAnyBody()
    {
        var f = Solve();
        var geometry = new RoofGeneratedMemberGeometry(f.Axis.Start, f.Axis.End);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(geometry, geometry,
            new(0, 0, 1), out var startDelta, out var endDelta, out var normalized, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(geometry, normalized));
        Assert.Equal(0, startDelta);
        Assert.Equal(0, endDelta);
        var bodies = Bindings(f.Generated);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(Keys(f.Generated), [], bodies, false, out var plan));
        Assert.Empty(plan!.RebuildKeys);
        Assert.Empty(plan.RemoveBodyIdentities);
    }

    [Theory]
    [InlineData("CommandCancelled", "CommandFailed")]
    [InlineData("CommandFailed", "ClearPendingLiveGeometryState")]
    public void CancelOrFailure_DropsPendingExtend_WithoutReconciliation(string handler, string next)
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        var start = source.IndexOf("private void " + handler + "(", StringComparison.Ordinal);
        var end = source.IndexOf("private void " + next + "(", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("ClearPendingLiveGeometryState()", method);
        Assert.DoesNotContain("RefreshCandidates(", method);
        Assert.DoesNotContain("TryReconcile", method);
        Assert.DoesNotContain("StartTransaction", method);
    }

    [Fact]
    public void AcceptedEndpointGeometry_UsesExistingTargetedAdapter_BeforeGroupSync()
    {
        var source = Read("RoofGeneratedMemberManualEditService.cs");
        var start = source.IndexOf("private static bool TryAcceptUnlockedEdits(", StringComparison.Ordinal);
        var method = source[start..];
        var unchanged = method.IndexOf("if (unchanged)", StringComparison.Ordinal);
        var accepted = method.IndexOf("acceptedPlanIds.Add(id)", StringComparison.Ordinal);
        var gate = method.IndexOf("if (RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(globalCommandName))", StringComparison.Ordinal);
        var physical = method.IndexOf(".TryReconcileModifiedMembersInTransaction(", gate, StringComparison.Ordinal);
        Assert.True(unchanged >= 0 && accepted > unchanged && gate > accepted && physical > gate);
        Assert.Contains("acceptedPlanIds", method[physical..(physical + 650)]);
        var group = source.IndexOf("var groupSynced = RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);
        Assert.True(source.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal) < group);
        var builder = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        Assert.Contains("foreach (var id in acceptedPlanIds ?? modifiedIds)", builder);
        Assert.Contains("if (changedKeys.Count == 0) return true", builder);
        Assert.Contains("CreateSolid(members[key])", builder);
        Assert.DoesNotContain("GeometricExtents", builder);
        Assert.DoesNotContain("MassProperties", builder);
        Assert.Contains("RefreshModifiedAttachedManualRelatives", source);
        Assert.Contains("geometry, attachedChanged, Array.Empty<ObjectId>()", source);
    }

    private static void AssertTargetReplacement(RoofAutomaticRafterPhysicalModel before,
        RoofAutomaticRafterPhysicalModel after, string key)
    {
        var bodies = Bindings(before);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(Keys(after), [key], bodies, false, out var plan));
        Assert.Equal([key], plan!.RebuildKeys);
        Assert.Equal([bodies.Single(body => body.SemanticKey == key).BodyIdentity], plan.RemoveBodyIdentities);
        var final = bodies.Where(body => !plan.RemoveBodyIdentities.Contains(body.BodyIdentity))
            .Concat([new RoofOrdinaryPhysicalBinding("rebuilt", key)]).ToArray();
        Assert.Single(final, body => body.SemanticKey == key);
        Assert.Equal(before.Members.Count, final.Length);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(Keys(after), final.Select(body => body.SemanticKey).ToArray()));
        Assert.All(bodies.Where(body => body.SemanticKey != key), body => Assert.Contains(body, final));
    }

    private static RoofOrdinaryPhysicalBinding[] Bindings(RoofAutomaticRafterPhysicalModel model) =>
        model.Members.Select((member, index) => new RoofOrdinaryPhysicalBinding("old" + index, member.PhysicalIdentity)).ToArray();
    private static string[] Keys(RoofAutomaticRafterPhysicalModel model) => model.Members.Select(member => member.PhysicalIdentity).ToArray();
    private sealed record Fixture(HipRoofGeometry Roof, RoofFaceRafterLayout Faces, RoofRafterLayout Layout,
        RoofAutomaticRafterPhysicalModel Generated, RoofGeneratedMemberKey Key, RoofSegment3D Axis);
    private static Fixture Solve()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var solved = RoofGeometrySolver.Solve(new(footprint.Footprint!, new(45), RoofKind.Hip));
        var roof = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", roof.Topology, faces, layout, 3000, 80, 125, new(), out var generated));
        var target = generated!.Members.First(member => member.StartBoundaryRole == RoofRafterBoundaryRole.Eave && member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return new(roof, faces, layout, generated, target.MemberKey, target.PlanAxis);
    }
    private static RoofAutomaticRafterPhysicalModel Build(Fixture f, RoofGeneratedMemberOverride? edit)
    {
        var replay = RoofGeneratedMemberReplayPlanner.Create(f.Layout, 0, new(0, 0, 1), edit is null ? [] : [edit]);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
            3000, 80, 125, new(), replay, null, out var generated, out var reason), reason);
        return generated!;
    }
    private static RoofAutomaticRafterPhysicalModel Append(Fixture f, RoofAttachedManualTimberData data, RoofSegment3D axis)
    {
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(data, axis, 80, 125)], 3000, new(), null, out var model, out var reason), reason);
        return model!;
    }
    private static RoofPoint3D At(RoofSegment3D axis, double t) => new(axis.Start.X + (axis.End.X - axis.Start.X) * t,
        axis.Start.Y + (axis.End.Y - axis.Start.Y) * t, 0);
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
