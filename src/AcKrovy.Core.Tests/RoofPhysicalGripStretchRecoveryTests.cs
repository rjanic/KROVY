using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Recovery routing, semantic builders and body/GROUP planning.
/// Native Solid3d replacement and callback ordering still require HOST validation.</summary>
public sealed class RoofPhysicalGripStretchRecoveryTests
{
    [Theory]
    [InlineData(RoofEditState.Locked, false, RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofEditState.Unlocked, false, RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofEditState.Locked, true, RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofEditState.Unlocked, true, RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofEditState.Locked, true, RoofAttachedManualOrigin.Split)]
    [InlineData(RoofEditState.Unlocked, true, RoofAttachedManualOrigin.Split)]
    public void PhysicalOnlyGrip_RecoversAuthoritativeMember_InBothRoofStates(
        RoofEditState state, bool attached, RoofAttachedManualOrigin origin)
    {
        var f = Solve();
        var definition = new RoofDefinitionData(5, RoofKind.Hip, 45, EditState: state);
        var child = Child(f, f.Axis) with { Origin = origin };
        var authoritative = attached ? Append(f, child, f.Axis) : f.Generated;
        var key = attached ? RoofAttachedManualIdentityRules.PhysicalKey(child)
            : f.Generated.Members.Single(m => m.MemberKey == f.Key).PhysicalIdentity;

        Assert.True(RoofPhysicalStretchRules.ShouldRecover("GRIP_STRETCH", sourceModified: false));
        Assert.True(RoofPhysicalStretchRules.ShouldRejectDirectEdit(acceptedPlanEdit: false));
        // Recreate from the unchanged Plan2D/semantic inputs. A tampered solid
        // contributes only its persisted key, never coordinates or a transform.
        var restored = attached ? Append(f, child, f.Axis) : Build(f, null);
        var expected = authoritative.Members.Single(m => m.PhysicalIdentity == key);
        var actual = restored.Members.Single(m => m.PhysicalIdentity == key);
        Assert.Equal(expected.PlanAxis, actual.PlanAxis);
        Assert.Equal(expected.SolidVertices, actual.SolidVertices);
        Assert.Equal(expected.MemberKey, actual.MemberKey);
        Assert.Equal(expected.AttachedManualIdentity, actual.AttachedManualIdentity);
        Assert.Equal(state, definition.EditState);
        Assert.Empty(definition.Overrides);
        if (attached) Assert.Equal(child.SemanticIdentity, actual.AttachedManualIdentity);
        AssertReplacement(authoritative, restored, key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedGrip_AcceptedPlanGeometryWins_WithSameGeneratedKeyOrAttachedUuid(bool attached)
    {
        var f = Solve();
        var finalAxis = new RoofSegment3D(f.Axis.Start, At(f.Axis, 0.8));
        var child = Child(f, f.Axis);
        var before = attached ? Append(f, child, f.Axis) : f.Generated;
        RoofAutomaticRafterPhysicalModel after;
        string key;
        if (attached)
        {
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(f.Axis.Start, f.Axis.End,
                finalAxis.Start, finalAxis.End, out var relative));
            var accepted = child with { RelativeSegment = relative };
            Assert.Equal(child.SemanticIdentity, accepted.SemanticIdentity);
            after = Append(f, accepted, finalAxis);
            key = RoofAttachedManualIdentityRules.PhysicalKey(accepted);
        }
        else
        {
            Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(new(f.Axis.Start, f.Axis.End),
                new(finalAxis.Start, finalAxis.End), new(0, 0, 1), f.Key, "R1", out var accepted));
            after = Build(f, accepted);
            key = after.Members.Single(m => m.MemberKey == f.Key).PhysicalIdentity;
        }
        Assert.True(RoofPhysicalStretchRules.ShouldRecover("GRIP_STRETCH", sourceModified: false));
        Assert.False(RoofPhysicalStretchRules.ShouldRejectDirectEdit(acceptedPlanEdit: true));
        var result = after.Members.Single(m => m.PhysicalIdentity == key);
        Assert.Equal(finalAxis, result.PlanAxis);
        Assert.NotEqual(before.Members.Single(m => m.PhysicalIdentity == key).SolidVertices, result.SolidVertices);
        AssertReplacement(before, after, key);
        Assert.All(before.Members.Where(m => m.PhysicalIdentity != key), old =>
            Assert.Equal(old.SolidVertices, after.Members.Single(m => m.PhysicalIdentity == old.PhysicalIdentity).SolidVertices));
    }

    [Fact]
    public void Adapter_RecoversAtCommandEnd_WithoutLockGate_UsingMoveRecoveryAndCurrentPlanState()
    {
        var live = Read("RoofLiveResizeService.cs");
        var candidate = Slice(live, "if (RoofPhysicalStretchRules.ShouldRecover(globalCommandName, sourceModified: false)",
            "if (RoofDisplayStore.Read(entity).Exists)");
        Assert.Contains("RoofPhysical3DGeneratedStore.Read(entity)", candidate);
        Assert.DoesNotContain("EditState", candidate);
        var apply = Slice(live, "private static void ApplyDerivedPhysicalStretchTampers(",
            "internal static bool TryVerifyStretchPhysicalState(");
        Assert.DoesNotContain("EditState", apply);
        Assert.Contains("TryRestoreStretchPhysicalInTransaction", apply);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", apply);
        Assert.Contains("TryVerifyStretchPhysicalState", apply);
        Assert.Contains("TryFinalizeRestoredPhysicalGroup", apply);
        Assert.Contains("Command_Roof_DerivedPhysicalMoveRejected", apply);
        Assert.True(live.IndexOf("RoofGeneratedMemberManualEditService.ProcessOwners(", StringComparison.Ordinal) <
            live.IndexOf("ApplyDerivedPhysicalStretchTampers(", StringComparison.Ordinal));
        var lifecycle = Slice(Read("RoofPhysical3DLifecycleService.cs"),
            "public static bool TryRestoreStretchPhysicalInTransaction(",
            "public static void CleanupStillErasedSourceInTransaction(");
        Assert.Contains("RoofDefinitionPersistence.Restore", lifecycle);
        Assert.Contains("TryRestoreMovedPhysicalMembersInTransaction", lifecycle);
        Assert.Contains("id.IsErased", lifecycle); // Already rebuilt accepted bodies are skipped.
        var move = Slice(Read("RoofOrdinaryRafterSolidMaterializationService.cs"),
            "public static bool TryRestoreMovedPhysicalMembersInTransaction(",
            "public static bool TryVerifyRestoredPhysicalMembersInTransaction(");
        Assert.DoesNotContain("EditState", move);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", move);
        Assert.Contains("RoofAttachedManualIdentityRules.PhysicalKey(data)", move);
        Assert.Contains("movedMemberIds.Select(id => authoritativeLines[id])", move);
        Assert.DoesNotContain("GeometricExtents", move);
        Assert.DoesNotContain("TransformBy", move);
        Assert.DoesNotContain("OpenMode.ForWrite", move);
        var commandEnd = Slice(Read("LiveGeometrySynchronizationService.cs"),
            "private void CommandEnded(", "private void CommandCancelled(");
        Assert.Contains("RefreshCandidates(", commandEnd);
        Assert.Contains("MaintenanceComplete", commandEnd);
        Assert.Contains("isUndoRedo", commandEnd);
    }

    private static void AssertReplacement(RoofAutomaticRafterPhysicalModel before,
        RoofAutomaticRafterPhysicalModel after, string key)
    {
        var bodies = before.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("old" + i, m.PhysicalIdentity)).ToArray();
        var expected = after.Members.Select(m => m.PhysicalIdentity).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(expected, [key, key], bodies, false, out var plan));
        Assert.Equal([key], plan!.RebuildKeys);
        Assert.Equal([bodies.Single(b => b.SemanticKey == key).BodyIdentity], plan.RemoveBodyIdentities);
        var final = bodies.Where(b => !plan.RemoveBodyIdentities.Contains(b.BodyIdentity))
            .Concat([new RoofOrdinaryPhysicalBinding("rebuilt", key)]).ToArray();
        Assert.Single(final, b => b.SemanticKey == key);
        Assert.Equal(bodies.Length, final.Length);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(expected, final.Select(b => b.SemanticKey).ToArray()));
        var otherGroupMembers = new[] { "source", "Plan2D", "annotations" };
        var originalGroup = otherGroupMembers.Concat(bodies.Select(b => b.BodyIdentity)).ToArray();
        var expectedGroup = otherGroupMembers.Concat(final.Select(b => b.BodyIdentity)).ToArray();
        var canonicalGroup = originalGroup.Where(id => !plan.RemoveBodyIdentities.Contains(id))
            .Concat(["rebuilt"]).ToArray();
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(canonicalGroup, expectedGroup));
        Assert.DoesNotContain(plan.RemoveBodyIdentities.Single(), canonicalGroup);
        Assert.All(bodies.Where(b => b.SemanticKey != key), b => Assert.Contains(b, final));
    }

    private sealed record Fixture(HipRoofGeometry Roof, RoofFaceRafterLayout Faces, RoofRafterLayout Layout,
        RoofAutomaticRafterPhysicalModel Generated, RoofGeneratedMemberKey Key, RoofSegment3D Axis);
    private static Fixture Solve()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint.Footprint!, new(45), RoofKind.Hip)).Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", roof.Topology, faces, layout, 3000, 80, 125, new(), out var model));
        var target = model!.Members.First(m => m.StartBoundaryRole == RoofRafterBoundaryRole.Eave && m.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return new(roof, faces, layout, model, target.MemberKey, target.PlanAxis);
    }
    private static RoofAutomaticRafterPhysicalModel Build(Fixture f, RoofGeneratedMemberOverride? edit)
    {
        var replay = RoofGeneratedMemberReplayPlanner.Create(f.Layout, 0, new(0, 0, 1), edit is null ? [] : [edit]);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
            3000, 80, 125, new(), replay, out var model));
        return model!;
    }
    private static RoofAttachedManualTimberData Child(Fixture f, RoofSegment3D axis)
    {
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(f.Axis.Start, f.Axis.End,
            axis.Start, axis.End, out var relative));
        return new(4, "AB", "CHILD", RoofTimberChildRole.AttachedManual, f.Key, relative,
            RoofAttachedManualOrigin.Copy, RoofAttachedManualIdentityRules.Create());
    }
    private static RoofAutomaticRafterPhysicalModel Append(Fixture f, RoofAttachedManualTimberData child, RoofSegment3D axis)
    {
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(child, axis, 80, 125)], 3000, new(), null, out var model, out var reason), reason);
        return model!;
    }
    private static RoofPoint3D At(RoofSegment3D axis, double t) => new(axis.Start.X + (axis.End.X - axis.Start.X) * t,
        axis.Start.Y + (axis.End.Y - axis.Start.Y) * t, 0);
    private static string Slice(string source, string start, string end)
    {
        var offset = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(offset >= 0, start);
        return source[offset..source.IndexOf(end, offset, StringComparison.Ordinal)];
    }
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
