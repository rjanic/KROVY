using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Real Core geometry/codec/planner coverage. Native DWG transactions,
/// command ordering and Undo/Redo remain separate HOST retests.</summary>
public sealed class RoofAttachedManualPhysicalLifecycleTests
{
    private sealed record Fixture(HipRoofGeometry Roof, RoofFaceRafterLayout Faces,
        RoofRafterLayout Layout, RoofAutomaticRafterPhysicalModel Generated);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyMigration_IsDeterministic_AndPersistsAcrossCadRebinding(int schema)
    {
        var f = Solve();
        var legacy = Child(f, Axis(f), RoofAttachedManualOrigin.Copy) with { SchemaVersion = schema, SemanticIdentity = null };
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(RoofAttachedManualTimberDataCodec.Encode(legacy), out var read));
        var migrated = RoofAttachedManualIdentityRules.Upgrade(read!);
        Assert.Equal(4, migrated.SchemaVersion);
        Assert.Equal(RoofAttachedManualIdentityRules.Resolve(legacy), migrated.SemanticIdentity);
        Assert.Equal(migrated, RoundTrip(migrated));
        Assert.Equal(RoofAttachedManualIdentityRules.PhysicalKey(migrated),
            RoofAttachedManualIdentityRules.PhysicalKey(migrated with { ChildIdentity = "REBIND", RoofOwnerReference = "NEWOWNER" }));
        Assert.NotEqual(RoofAttachedManualIdentityRules.PhysicalKey(legacy),
            RoofAttachedManualIdentityRules.PhysicalKey(legacy with { ChildIdentity = "OTHER" }));
    }

    [Fact]
    public void LegacyUnanchoredChild_IsReadable_ButDoesNotInventGeometry()
    {
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode("1|AB|OLD|AttachedManual", out var old));
        Assert.Equal(old, RoofAttachedManualIdentityRules.Upgrade(old!));
        var f = Solve();
        Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(old!, Axis(f), 80, 125)], 3000, new(), null, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad-identity")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void CurrentCodec_RejectsMalformedPersistentIdentity(string identity)
    {
        var f = Solve();
        var payload = RoofAttachedManualTimberDataCodec.Encode(Child(f, Axis(f), RoofAttachedManualOrigin.Copy));
        var fields = payload.Split('|');
        fields[^1] = identity;
        Assert.False(RoofAttachedManualTimberDataCodec.TryDecode(string.Join('|', fields), out _));
        Assert.False(RoofAttachedManualTimberDataCodec.TryDecode(string.Join('|', fields.Take(14)), out _));
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Overlap)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Overlap)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Overlap)]
    public void AttachedBuilder_ReusesRoofPlaneSectionAndBoundaryPolicies(LowerEndCutMode lower, RidgeJoinMode ridge)
    {
        var f = Solve();
        var axis = Axis(f);
        var child = Child(f, axis, RoofAttachedManualOrigin.Copy);
        var built = Append(f, [new(child, axis, 80, 125)], new(lower, ridge));
        var actual = built.Members.Single(m => m.AttachedManualIdentity is not null);
        Assert.Equal(axis, actual.PlanAxis);
        Assert.Equal(80, actual.WidthMm);
        Assert.Equal(125, actual.HeightMm);
        Assert.Equal(RoofAttachedManualIdentityRules.PhysicalKey(child), actual.PhysicalIdentity);
        Assert.NotEqual(f.Generated.Members[0].PhysicalIdentity, actual.PhysicalIdentity);
        var face = f.Roof.Topology.Faces.Single(face => face.SourceEdgeIndex == actual.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(f.Roof.Topology, face, out var normal));
        var origin = f.Roof.Topology.Nodes[face.BoundaryNodeIndices[0]];
        var top = actual.RidgeOverlapCut?.TopFaceVertices ?? actual.HorizontalCut?.TopFaceVertices ?? actual.SolidVertices.Take(4).ToArray();
        Assert.All(top, p => Assert.Equal(0, normal.X * (p.X - origin.X) + normal.Y * (p.Y - origin.Y) + normal.Z * (p.Z - origin.Z - 3000), 5));
        Assert.All(built.Members.Where(m => m.AttachedManualIdentity is null), m => Assert.Same(f.Generated.Members.Single(g => g.MemberKey == m.MemberKey), m));
        Assert.Equal(built.Members.Count, built.Members.Select(m => m.PhysicalIdentity).Distinct().Count());
    }

    [Theory]
    [InlineData("MOVE")]
    [InlineData("TRIM")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("MIRROR_YES")]
    public void AttachedEdit_RebuildsOnlySameIdentity_FromFinalPlanAxis(string command)
    {
        var f = Solve();
        var original = Axis(f);
        var axis = command == "MOVE" ? Translate(original, 50, 0) : command == "MIRROR_YES" ? Reflect(original) : new RoofSegment3D(original.Start, At(original, 0.75));
        var child = Child(f, original, RoofAttachedManualOrigin.Copy);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(original.Start, original.End, axis.Start, axis.End, out var relative));
        var after = child with { RelativeSegment = relative };
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(original.Start, original.End, after.RelativeSegment!, out var replayStart, out var replayEnd));
        Assert.True(replayStart.DistanceTo(axis.Start) < 1e-5);
        Assert.True(replayEnd.DistanceTo(axis.End) < 1e-5);
        var model = Append(f, [new(after, axis, 80, 125)]);
        var key = RoofAttachedManualIdentityRules.PhysicalKey(child);
        Assert.Equal(key, model.Members.Single(m => m.AttachedManualIdentity is not null).PhysicalIdentity);
        Assert.Equal(axis, model.Members.Single(m => m.AttachedManualIdentity is not null).PlanAxis);
        var bodies = model.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("body" + i, m.PhysicalIdentity)).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(model.Members.Select(m => m.PhysicalIdentity).ToArray(), [key], bodies, true, out var plan));
        Assert.Equal([key], plan!.RebuildKeys);
        Assert.Single(plan.RemoveBodyIdentities);
        Assert.Equal(key, RoundTrip(after).SemanticIdentity is { } ? RoofAttachedManualIdentityRules.PhysicalKey(RoundTrip(after)) : "missing");
    }

    [Theory]
    [InlineData("BREAK", false)]
    [InlineData("BREAK", true)]
    [InlineData("BREAKATPOINT", false)]
    public void Break_RetainsGeneratedKey_AndCreatesOnlyTwoRetainedPhysicalSegments(string command, bool twoPoints)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.RequiresOrdinaryPhysicalReconcile(command));
        var f = Solve();
        var source = Axis(f);
        var first = new RoofSegment3D(source.Start, At(source, 0.4));
        var second = new RoofSegment3D(At(source, twoPoints ? 0.6 : 0.4), source.End);
        var key = Key(f);
        var edit = Classify(f, first);
        var replay = RoofGeneratedMemberReplayPlanner.Create(f.Layout, 0, new(0, 0, 1), [edit]);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
            3000, 80, 125, new(), replay, null, out var generated, out var reason), reason);
        var child = Child(f, second, RoofAttachedManualOrigin.Split);
        Assert.Equal(4, child.SchemaVersion);
        Assert.NotNull(child.SemanticIdentity);
        var broken = Append(f with { Generated = generated! }, [new(child, second, 80, 125)]);
        var retained = broken.Members.Single(m => m.AttachedManualIdentity is null && m.MemberKey == key);
        var added = broken.Members.Single(m => m.AttachedManualIdentity is not null);
        Assert.Equal(key, retained.MemberKey);
        Assert.Equal(first, retained.PlanAxis);
        Assert.Equal(second, added.PlanAxis);
        Assert.Equal(RoofRafterBoundaryRole.Free, retained.EndBoundaryRole);
        Assert.Equal(RoofRafterBoundaryRole.Free, added.StartBoundaryRole);
        Assert.Equal(f.Generated.Members.Count + 1, broken.Members.Count);
        Assert.True(retained.PhysicalLengthMm < f.Generated.Members.Single(m => m.MemberKey == key).PhysicalLengthMm);
        Assert.Null(retained.RidgeOverlapCut);
        Assert.Equal(source.Start.DistanceTo(source.End) * (twoPoints ? 0.8 : 1),
            first.Start.DistanceTo(first.End) + second.Start.DistanceTo(second.End), 5);
        var physicalKey = retained.PhysicalIdentity;
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(broken.Members.Select(m => m.PhysicalIdentity).ToArray(),
            [physicalKey], f.Generated.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("old" + i, m.PhysicalIdentity)).ToArray(), true, out var plan));
        Assert.Equal(2, plan!.RebuildKeys.Count);
        Assert.Single(plan.RemoveBodyIdentities);
        Assert.Contains(physicalKey, plan.RebuildKeys);
        Assert.Contains(added.PhysicalIdentity, plan.RebuildKeys);
        var reconciledKeys = f.Generated.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("old" + i, m.PhysicalIdentity))
            .Where(body => !plan.RemoveBodyIdentities.Contains(body.BodyIdentity))
            .Select(body => body.SemanticKey).Concat(plan.RebuildKeys).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(
            broken.Members.Select(m => m.PhysicalIdentity).ToArray(),
            reconciledKeys));
    }

    [Theory]
    [InlineData("COPY", 1)]
    [InlineData("COPY", 3)]
    [InlineData("MIRROR_NO", 1)]
    public void Clone_PreservesSourceBody_CreatesDistinctAttachedBodies_AndDiscardsCollateral(string command, int copies)
    {
        var f = Solve();
        var children = Enumerable.Range(1, copies).Select(i =>
        {
            var axis = command == "COPY" ? Translate(Axis(f), i * 50, 0) : Reflect(Axis(f));
            return new RoofAttachedManualPhysicalInput(Child(f, axis, RoofAttachedManualOrigin.Copy), axis, 80, 125);
        }).ToArray();
        var model = Append(f, children);
        var source = f.Generated.Members.Single(m => m.MemberKey == Key(f));
        Assert.Same(source, model.Members.Single(m => m.AttachedManualIdentity is null && m.MemberKey == source.MemberKey));
        var bindings = f.Generated.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("source" + i, m.PhysicalIdentity))
            .Concat([new("nativeClone", source.PhysicalIdentity, true)]).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(model.Members.Select(m => m.PhysicalIdentity).ToArray(), [], bindings, true, out var plan));
        Assert.Equal(["nativeClone"], plan!.RemoveBodyIdentities);
        Assert.Equal(copies, plan.RebuildKeys.Count);
        Assert.DoesNotContain(source.PhysicalIdentity, plan.RebuildKeys);
        Assert.All(children, child => Assert.Contains(RoofAttachedManualIdentityRules.PhysicalKey(child.Metadata), plan.RebuildKeys));
        Assert.Equal(copies, children.Select(c => c.Metadata.SemanticIdentity).Distinct().Count());
    }

    [Fact]
    public void MirrorYes_ReplaysMirroredGeometry_WithSameGeneratedKeyAndReservedNumber()
    {
        var f = Solve();
        var reflected = Reflect(Axis(f));
        var edit = Classify(f, reflected);
        Assert.False(edit.Suppressed);
        Assert.Equal(Key(f), edit.Key);
        Assert.Equal("R7", edit.ReservedElementId);
        var replay = RoofGeneratedMemberReplayPlanner.Create(f.Layout, 0, new(0, 0, 1), [edit]);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout, 3000,
            80, 125, new(), replay, null, out var after, out var reason), reason);
        Assert.Equal(f.Generated.Members.Count, after!.Members.Count);
        var replacement = after.Members.Single(m => m.MemberKey == Key(f));
        Assert.True(replacement.PlanAxis.Start.DistanceTo(reflected.Start) < 1e-5);
        Assert.True(replacement.PlanAxis.End.DistanceTo(reflected.End) < 1e-5);
        Assert.Null(replacement.AttachedManualIdentity);
        Assert.Equal(f.Generated.Members.Single(m => m.MemberKey == Key(f)).PhysicalIdentity, replacement.PhysicalIdentity);
        var actualFace = f.Roof.Topology.Faces.Single(face => face.SourceEdgeIndex == replacement.SourceFaceIndex);
        Assert.True(RoofFootprintContainmentRules.IsSegmentInsideOrOnBoundary(new(reflected.Start.X, reflected.Start.Y),
            new(reflected.End.X, reflected.End.Y), actualFace.BoundaryNodeIndices.Select(i => new RoofPoint2D(f.Roof.Topology.Nodes[i].X, f.Roof.Topology.Nodes[i].Y)).ToArray()));
    }

    [Theory]
    [InlineData("BREAK")]
    [InlineData("COPY")]
    [InlineData("MIRROR_NO")]
    [InlineData("MIRROR_YES")]
    public void SerializedSemanticSnapshot_ReloadAndUndoRedo_RebuildSameIdentitiesAndGeometry(string command)
    {
        var f = Solve();
        var axis = command == "BREAK" ? new RoofSegment3D(At(Axis(f), 0.5), Axis(f).End) : command == "COPY" ? Translate(Axis(f), 50, 0) : Reflect(Axis(f));
        var child = command == "MIRROR_YES" ? null : Child(f, axis, command == "BREAK" ? RoofAttachedManualOrigin.Split : RoofAttachedManualOrigin.Copy);
        var edit = command == "MIRROR_YES" ? Classify(f, axis) : command == "BREAK" ? Classify(f, new(Axis(f).Start, At(Axis(f), 0.5))) : null;
        Assert.True(RoofDefinitionDataCodec.TryDecode("5|Hip|45|45|0|None|4|CCW|10000|6000|Unlocked|", out var roofData, out _));
        var serialized = RoofDefinitionDataCodec.Encode(roofData! with { ManualOverrides = edit is null ? [] : [edit] });
        var childPayload = child is null ? null : RoofAttachedManualTimberDataCodec.Encode(child);
        RoofAutomaticRafterPhysicalModel Reload()
        {
            Assert.True(RoofDefinitionDataCodec.TryDecode(serialized, out var roof, out _));
            var replay = RoofGeneratedMemberReplayPlanner.Create(f.Layout, 0, new(0, 0, 1), roof!.Overrides);
            Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
                3000, 80, 125, new(), replay, null, out var generated, out var reason), reason);
            if (childPayload is null) return generated!;
            Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(childPayload, out var attached));
            var anchor = generated!.Members.Single(member => member.MemberKey == attached!.AnchorGeneratedMemberKey);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(anchor.PlanAxis.Start, anchor.PlanAxis.End,
                attached!.RelativeSegment!, out var start, out var end));
            Assert.True(start.DistanceTo(axis.Start) < 1e-5);
            Assert.True(end.DistanceTo(axis.End) < 1e-5);
            return Append(f with { Generated = generated! }, [new(attached, new(start, end), 80, 125)]);
        }
        var accepted = Reload();
        var undone = f.Generated; // Existing pre-command semantic snapshot; no fresh identity is minted.
        Assert.Equal(f.Layout.Rafters.Count, undone.Members.Count);
        Assert.DoesNotContain(undone.Members, m => m.AttachedManualIdentity is not null);
        var redone = Reload();
        Assert.Equal(accepted.Members.Select(m => m.PhysicalIdentity), redone.Members.Select(m => m.PhysicalIdentity));
        Assert.Equal(accepted.Members.Select(m => m.PlanAxis), redone.Members.Select(m => m.PlanAxis));
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(accepted.Members.Select(m => m.PhysicalIdentity).ToArray(),
            redone.Members.Select(m => m.PhysicalIdentity).ToArray()));
    }

    [Fact]
    public void AttachedEraseAndDormancy_RemoveOnlyMatchingBody_KeepSemanticIdentityForReactivation()
    {
        var f = Solve();
        var child = Child(f, Axis(f), RoofAttachedManualOrigin.Copy);
        var key = RoofAttachedManualIdentityRules.PhysicalKey(child);
        var withChild = Append(f, [new(child, Axis(f), 80, 125)]);
        var dormant = Append(f, [new(child, Axis(f), 80, 125, false)]);
        Assert.Equal(f.Generated.Members.Count, dormant.Members.Count);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(dormant.Members.Select(m => m.PhysicalIdentity).ToArray(), [],
            withChild.Members.Select((m, i) => new RoofOrdinaryPhysicalBinding("body" + i, m.PhysicalIdentity)).ToArray(), true, out var plan));
        Assert.Single(plan!.RemoveBodyIdentities);
        Assert.Empty(plan.RebuildKeys);
        Assert.Equal(key, Append(f, [new(RoundTrip(child), Axis(f), 80, 125)]).Members.Single(m => m.AttachedManualIdentity is not null).PhysicalIdentity);
    }

    [Fact]
    public void DuplicateSemanticIdentity_FailsBeforeMaterialization()
    {
        var f = Solve();
        var child = Child(f, Axis(f), RoofAttachedManualOrigin.Copy);
        Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(child, Axis(f), 80, 125), new(child with { ChildIdentity = "OTHER" }, Translate(Axis(f), 50, 0), 80, 125)],
            3000, new(), null, out _, out _));
    }

    [Theory]
    [InlineData(0, 125, 0)]
    [InlineData(80, -1, 0)]
    [InlineData(80, 125, 10)]
    [InlineData(double.NaN, 125, 0)]
    public void InvalidSectionOrNonPlanarInput_FailsClosed(double width, double height, double z)
    {
        var f = Solve();
        var axis = Axis(f) with { Start = Axis(f).Start with { Z = z } };
        Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(Child(f, Axis(f), RoofAttachedManualOrigin.Copy), axis, width, height)], 3000, new(), null, out _, out _));
    }

    [Fact]
    public void CanonicalGroupCountCannotHideDuplicateOrMissingPhysicalKeys()
    {
        Assert.False(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(["Generated", "Attached"], ["Generated", "Generated"]));
        Assert.False(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(["Generated", "Attached"], ["Generated", "Foreign"]));
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(["Generated", "Attached"], [],
            [new("oldA", "Generated"), new("oldB", "Generated")], true, out var plan));
        Assert.Equal(["oldA", "oldB"], plan!.RemoveBodyIdentities);
        Assert.Equal(["Attached", "Generated"], plan.RebuildKeys);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("orphan")]
    public void StrictExistingMemberEdit_RejectsDamagedPrestate(string damage)
    {
        RoofOrdinaryPhysicalBinding[] bodies = damage switch
        {
            "missing" => [], "duplicate" => [new("a", "key"), new("b", "key")],
            _ => [new("a", "key"), new("b", "orphan")],
        };
        Assert.False(RoofOrdinaryPhysicalReconciliationRules.TryPlan(["key"], ["key"], bodies, false, out _));
    }

    [Fact]
    public void NoOp_PreservesAllBindings_AndPlansNoPhysicalMutation()
    {
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(["Generated", "Attached"], [],
            [new("source", "Generated"), new("child", "Attached")], true, out var plan));
        Assert.Empty(plan!.RebuildKeys);
        Assert.Empty(plan.RemoveBodyIdentities);
    }

    [Fact]
    public void AttachedOnlyModel_WorksWithoutLiveGeneratedMember_UsingPersistedAnchorAndTopology()
    {
        var f = Solve();
        var child = Child(f, Axis(f), RoofAttachedManualOrigin.Copy);
        var empty = new RoofAutomaticRafterPhysicalModel("AB", []);
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology,
            new(0, [], string.Empty), empty, [new(child, Axis(f), 80, 125)], 3000, new(), null, out var model, out var reason), reason);
        Assert.Single(model!.Members);
        Assert.Equal(RoofAttachedManualIdentityRules.PhysicalKey(child), model.Members[0].PhysicalIdentity);
    }

    [Fact]
    public void IdentityCaseCannotBypassDuplicatePhysicalIdentityValidation()
    {
        var f = Solve();
        var child = Child(f, Axis(f), RoofAttachedManualOrigin.Copy);
        var other = child with { ChildIdentity = "OTHER", SemanticIdentity = child.SemanticIdentity!.ToUpperInvariant() };
        Assert.Equal(RoofAttachedManualIdentityRules.PhysicalKey(child), RoofAttachedManualIdentityRules.PhysicalKey(other));
        Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(child, Axis(f), 80, 125), new(other, Translate(Axis(f), 50, 0), 80, 125)], 3000, new(), null, out _, out _));
    }

    [Fact]
    public void RotatedAttachedAxis_HasCorrectRoofPlane_WithoutChangingGeneratedIdentity()
    {
        var f = Solve();
        var original = Axis(f);
        var axis = new RoofSegment3D(At(original, 0.2), At(original, 0.7) with { X = original.End.X + 120 });
        var child = Child(f, axis, RoofAttachedManualOrigin.Copy);
        var model = Append(f, [new(child, axis, 80, 125)]);
        var member = model.Members.Single(m => m.AttachedManualIdentity is not null);
        Assert.Equal(RoofRafterBoundaryRole.Free, member.StartBoundaryRole);
        Assert.Equal(RoofRafterBoundaryRole.Free, member.EndBoundaryRole);
        Assert.True(member.PhysicalLengthMm > axis.Start.DistanceTo(axis.End));
        var face = f.Roof.Topology.Faces.Single(item => item.SourceEdgeIndex == member.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(f.Roof.Topology, face, out var normal));
        var origin = f.Roof.Topology.Nodes[face.BoundaryNodeIndices[0]];
        Assert.All(member.SolidVertices.Take(4), vertex => Assert.Equal(0,
            normal.X * (vertex.X - origin.X) + normal.Y * (vertex.Y - origin.Y) + normal.Z * (vertex.Z - origin.Z - 3000), 5));
    }

    [Fact]
    public void PlannerResult_ProducesExactlyOneBodyPerFinalIdentity_WhileRetainingUnchangedSourceBinding()
    {
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(["source", "childA", "childB"], [],
            [new("originalSource", "source"), new("native1", "source", true), new("native2", "source", true)], true, out var plan));
        var bodies = new[] { new RoofOrdinaryPhysicalBinding("originalSource", "source"), new("native1", "source", true), new("native2", "source", true) };
        var final = bodies.Where(body => !plan!.RemoveBodyIdentities.Contains(body.BodyIdentity))
            .Concat(plan!.RebuildKeys.Select(key => new RoofOrdinaryPhysicalBinding("built:" + key, key))).ToArray();
        Assert.Contains(final, body => body.BodyIdentity == "originalSource");
        Assert.DoesNotContain(final, body => body.Collateral);
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(["source", "childA", "childB"], final.Select(body => body.SemanticKey).ToArray()));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(final.Select(body => body.BodyIdentity).ToArray(), final.Select(body => body.BodyIdentity).ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(120)]
    public void MissingNativeAnchor_EditPersistsRelativeGeometryUsingProvenPrecommandBasis(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var start = new RoofPoint3D(110, 220, 0);
        var end = new RoofPoint3D(start.X + 1000 * Math.Cos(radians), start.Y + 1000 * Math.Sin(radians), 0);
        var childStart = new RoofPoint3D(300, 500, 0);
        var childEnd = new RoofPoint3D(750, 820, 0);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(start, end, childStart, childEnd, out var relative));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryRecoverAnchorBasis(relative, childStart, childEnd, out var recoveredStart, out var recoveredEnd));
        Assert.True(recoveredStart.DistanceTo(start) < 1e-5);
        var moved = new RoofPoint3D(childStart.X + 125, childStart.Y - 50, 0);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(recoveredStart, recoveredEnd, moved, childEnd, out var accepted));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(start, end, accepted, out var replayedStart, out var replayedEnd));
        Assert.True(replayedStart.DistanceTo(moved) < 1e-5);
        Assert.True(replayedEnd.DistanceTo(childEnd) < 1e-5);
        Assert.False(RoofAttachedManualRelativeGeometryRules.TryRecoverAnchorBasis(relative,
            childStart, childEnd with { X = childEnd.X + 25 }, out _, out _));
    }

    [Theory]
    [InlineData(RoofAttachedManualOrigin.Copy)]
    [InlineData(RoofAttachedManualOrigin.Split)]
    public void CopyOfAttachedMember_KeepsSourceIdentityAndBodyBinding_AndCreatesFreshChild(RoofAttachedManualOrigin origin)
    {
        var f = Solve();
        var axis = new RoofSegment3D(At(Axis(f), 0.3), Axis(f).End);
        var source = Child(f, axis, origin);
        var before = Append(f, [new(source, axis, 80, 125)]);
        var copyAxis = Translate(axis, 50, 0);
        var clone = Child(f, copyAxis, RoofAttachedManualOrigin.Copy);
        var after = Append(f, [new(source, axis, 80, 125), new(clone, copyAxis, 80, 125)]);
        var sourceKey = RoofAttachedManualIdentityRules.PhysicalKey(source);
        var cloneKey = RoofAttachedManualIdentityRules.PhysicalKey(clone);
        Assert.NotEqual(sourceKey, cloneKey);
        Assert.Equal(source, RoundTrip(source));
        Assert.Equal(axis, after.Members.Single(member => member.PhysicalIdentity == sourceKey).PlanAxis);
        var bodies = before.Members.Select((member, i) => new RoofOrdinaryPhysicalBinding("old" + i, member.PhysicalIdentity))
            .Concat([new("nativeAttachedClone", sourceKey, true)]).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(after.Members.Select(member => member.PhysicalIdentity).ToArray(),
            [], bodies, true, out var plan));
        Assert.Equal([cloneKey], plan!.RebuildKeys);
        Assert.Equal(["nativeAttachedClone"], plan.RemoveBodyIdentities);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void CopyMultiple_PreservesGeneratedSources_RebuildsEachCloneOnce_AndReloadsStableIdentities(int count, bool mixed)
    {
        var f = Solve();
        var children = Enumerable.Range(1, count).Select(index =>
        {
            var axis = Translate(Axis(f), index * 30, 0);
            return new RoofAttachedManualPhysicalInput(Child(f, axis, RoofAttachedManualOrigin.Copy) with
                { ChildIdentity = "CLONE" + index }, axis, 80, 125);
        }).ToArray();
        var after = Append(f, children);
        var originals = f.Generated.Members;
        Assert.Equal(originals.Count + count, after.Members.Count);
        foreach (var original in originals)
            Assert.Equal(original, after.Members.Single(member => member.PhysicalIdentity == original.PhysicalIdentity));
        Assert.Equal(count, children.Select(child => child.Metadata.SemanticIdentity).Distinct().Count());
        var bodies = originals.Select((member, index) => new RoofOrdinaryPhysicalBinding("old" + index, member.PhysicalIdentity))
            .Concat(mixed ? Enumerable.Range(1, count).Select(index => new RoofOrdinaryPhysicalBinding(
                "nativeSolid" + index, originals.Single(member => member.MemberKey == Key(f)).PhysicalIdentity, true)) : []).ToArray();
        var desired = after.Members.Select(member => member.PhysicalIdentity).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(desired, [], bodies, true, out var plan));
        Assert.Equal(count, plan!.RebuildKeys.Count);
        Assert.Equal(mixed ? count : 0, plan.RemoveBodyIdentities.Count);
        Assert.Equal(children.Select(child => RoofAttachedManualIdentityRules.PhysicalKey(child.Metadata)).Order(), plan.RebuildKeys.Order());
        var canonicalBodies = originals.Select((member, index) => new RoofOrdinaryPhysicalBinding("old" + index, member.PhysicalIdentity))
            .Concat(plan.RebuildKeys.Select((key, index) => new RoofOrdinaryPhysicalBinding("new" + index, key))).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(desired, canonicalBodies.Select(body => body.SemanticKey).ToArray()));
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(desired, [], canonicalBodies, true, out var repeated));
        Assert.Empty(repeated!.RebuildKeys);
        Assert.Empty(repeated.RemoveBodyIdentities);
        var reloaded = Append(f, children.Select(child => child with { Metadata = RoundTrip(child.Metadata) }).ToArray());
        Assert.Equal(after.Members.Select(member => member.PhysicalIdentity), reloaded.Members.Select(member => member.PhysicalIdentity));
        Assert.Equal(after.Members.Select(member => member.PlanAxis), reloaded.Members.Select(member => member.PlanAxis));
        Assert.Equal(originals.Count, f.Generated.Members.Count); // Undo snapshot has no semantic clone.
    }

    [Theory]
    [InlineData("BREAK", false, RoofAttachedManualOrigin.Copy)]
    [InlineData("BREAK", true, RoofAttachedManualOrigin.Copy)]
    [InlineData("BREAKATPOINT", false, RoofAttachedManualOrigin.Copy)]
    [InlineData("BREAK", false, RoofAttachedManualOrigin.Split)]
    [InlineData("BREAK", true, RoofAttachedManualOrigin.Split)]
    [InlineData("BREAKATPOINT", false, RoofAttachedManualOrigin.Split)]
    public void BreakOfAttachedMember_RejectsTransientDuplicateUuid_UntilSplitPromotion_ThenKeepsSourceIdentity(
        string command, bool twoPoints, RoofAttachedManualOrigin origin)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand(command));
        var f = Solve();
        // A Split child can overlap its Generated anchor. Its persistent UUID must
        // survive another BREAK; the Generated geometry/key must remain unchanged.
        var originalAxis = origin == RoofAttachedManualOrigin.Copy ? Translate(Axis(f), 50, 0) : Axis(f);
        var original = Child(f, originalAxis, origin) with { ChildIdentity = "SOURCE" };
        var before = Append(f, [new(original, originalAxis, 80, 125)]);
        var retainedAxis = new RoofSegment3D(originalAxis.Start, At(originalAxis, 0.4));
        var addedAxis = new RoofSegment3D(At(originalAxis, twoPoints ? 0.6 : 0.4), originalAxis.End);
        // Native inheritance is legal temporarily, but may never become physical SSOT.
        Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
            [new(original, retainedAxis, 80, 125), new(original, addedAxis, 80, 125)],
            3000, new(), null, out _, out _));
        var retained = Child(f, retainedAxis, origin) with
        {
            ChildIdentity = original.ChildIdentity, SemanticIdentity = original.SemanticIdentity,
        };
        var added = Child(f, addedAxis, RoofAttachedManualOrigin.Split) with { ChildIdentity = "FRAGMENT" };
        Assert.Equal(original.AnchorGeneratedMemberKey, retained.AnchorGeneratedMemberKey);
        Assert.Equal(original.SemanticIdentity, retained.SemanticIdentity);
        Assert.NotEqual(retained.SemanticIdentity, added.SemanticIdentity);
        var after = Append(f, [new(retained, retainedAxis, 80, 125), new(added, addedAxis, 80, 125)]);
        var sourceKey = RoofAttachedManualIdentityRules.PhysicalKey(original);
        var fragmentKey = RoofAttachedManualIdentityRules.PhysicalKey(added);
        var bodies = before.Members.Select((member, index) => new RoofOrdinaryPhysicalBinding("old" + index, member.PhysicalIdentity)).ToArray();
        var desired = after.Members.Select(member => member.PhysicalIdentity).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(desired, [sourceKey], bodies, true, out var plan));
        Assert.Equal(new[] { sourceKey, fragmentKey }.Order(), plan!.RebuildKeys.Order());
        Assert.Single(plan.RemoveBodyIdentities); // Old full-length AttachedManual body.
        var final = bodies.Where(body => !plan.RemoveBodyIdentities.Contains(body.BodyIdentity))
            .Concat(plan.RebuildKeys.Select(key => new RoofOrdinaryPhysicalBinding("rebuilt:" + key, key))).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(desired, final.Select(body => body.SemanticKey).ToArray()));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(final.Select(body => body.BodyIdentity).ToArray(),
            final.Select(body => body.BodyIdentity).ToArray()));
        Assert.All(f.Generated.Members, member => Assert.Same(member, after.Members.Single(result => result.PhysicalIdentity == member.PhysicalIdentity)));
        Assert.Equal(retainedAxis, after.Members.Single(member => member.PhysicalIdentity == sourceKey).PlanAxis);
        Assert.Equal(addedAxis, after.Members.Single(member => member.PhysicalIdentity == fragmentKey).PlanAxis);
        Assert.Equal(before.Members.Count + 1, after.Members.Count);
        // Existing semantic snapshot before the edit is the Undo state; serialized
        // accepted state replays the same UUIDs and geometries for Redo/reload.
        Assert.Equal(original.SemanticIdentity, RoundTrip(original).SemanticIdentity);
        var reloaded = Append(f, [new(RoundTrip(retained), retainedAxis, 80, 125), new(RoundTrip(added), addedAxis, 80, 125)]);
        Assert.Equal(desired, reloaded.Members.Select(member => member.PhysicalIdentity).ToArray());
        Assert.Equal(after.Members.Select(member => member.PlanAxis), reloaded.Members.Select(member => member.PlanAxis));
    }

    private static RoofAutomaticRafterPhysicalModel Append(Fixture f, RoofAttachedManualPhysicalInput[] children,
        RoofAutomaticRafterPhysicalSettings? settings = null)
    {
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated, children,
            3000, settings ?? new(), null, out var model, out var reason), reason);
        return model!;
    }
    private static RoofAttachedManualTimberData RoundTrip(RoofAttachedManualTimberData data)
    {
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(RoofAttachedManualTimberDataCodec.Encode(data), out var read));
        return read!;
    }
    private static RoofAttachedManualTimberData Child(Fixture f, RoofSegment3D axis, RoofAttachedManualOrigin origin)
    {
        var anchor = Axis(f);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(anchor.Start, anchor.End, axis.Start, axis.End, out var relative));
        return new(4, "AB", "BINDING", RoofTimberChildRole.AttachedManual, Key(f), relative,
            origin, RoofAttachedManualIdentityRules.Create());
    }
    private static RoofGeneratedMemberOverride Classify(Fixture f, RoofSegment3D axis)
    {
        var source = Axis(f);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(new(source.Start, source.End), new(axis.Start, axis.End),
            new(0, 0, 1), Key(f), "R7", out var edit));
        return edit!;
    }
    private static RoofGeneratedMemberKey Key(Fixture f) => f.Generated.Members.First(m =>
        m.StartBoundaryRole == RoofRafterBoundaryRole.Eave && m.EndBoundaryRole == RoofRafterBoundaryRole.Ridge).MemberKey;
    private static RoofSegment3D Axis(Fixture f) => f.Generated.Members.Single(m => m.MemberKey == Key(f)).PlanAxis;
    private static RoofPoint3D At(RoofSegment3D axis, double t) => new(axis.Start.X + (axis.End.X - axis.Start.X) * t,
        axis.Start.Y + (axis.End.Y - axis.Start.Y) * t, 0);
    private static RoofSegment3D Translate(RoofSegment3D axis, double x, double y) =>
        new(new(axis.Start.X + x, axis.Start.Y + y, 0), new(axis.End.X + x, axis.End.Y + y, 0));
    private static RoofSegment3D Reflect(RoofSegment3D axis) => new(new(axis.Start.X, 6000 - axis.Start.Y, 0), new(axis.End.X, 6000 - axis.End.Y, 0));
    private static Fixture Solve()
    {
        var input = new RoofFootprintInput([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true);
        var footprint = RoofFootprintValidator.Validate(input);
        var solved = RoofGeometrySolver.Solve(new(footprint.Footprint!, new(45), RoofKind.Hip));
        var roof = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", roof.Topology, faces, layout, 3000, 80, 125,
            new(), out var model));
        return new(roof, faces, layout, model!);
    }
}
