using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

// The former axis-lock contract is superseded by native freeform endpoint XY.
public sealed class RoofOrdinaryFreeformGripTests
{
    private sealed record Fixture(HipRoofGeometry Roof, RoofFaceRafterLayout Faces,
        RoofRafterLayout Layout, RoofRafterGeometry Rafter, RoofGeneratedMemberGeometry Canonical);

    [Theory]
    [InlineData(true, 1000, 0)]
    [InlineData(false, 1000, 0)]
    [InlineData(true, 1200, 700)]
    [InlineData(false, 1200, 700)]
    [InlineData(true, 10000, 0)]
    [InlineData(false, 10000, 0)]
    [InlineData(true, 0, -200)]
    [InlineData(false, 0, 200)]
    public void NativeEndpointXY_GeneratedReplayAndRoofPlaneFrame(bool start, double dx, double dy)
    {
        var f = Solve();
        var native = Drag(f.Canonical, start, dx, dy);
        var grip = Accept(f.Canonical, native);
        Assert.False(grip.Clamped);
        Assert.Equal(start ? "Start" : "End", grip.Endpoint);
        Assert.Equal(native, grip.Geometry);
        Assert.Equal(start ? f.Canonical.End : f.Canonical.Start, start ? grip.Geometry.End : grip.Geometry.Start);
        if (dx != 0) Assert.True(Math.Abs(grip.PlanYawDeltaDegrees) > 0.1);
        var edit = Compose(f, grip, null);
        var persisted = Persist(edit);
        Assert.Equal("K2", persisted.ReservedElementId);
        var plan = Apply(f, persisted);
        AssertGeometry(grip.Geometry, plan);
        var baseline = Build(f, null, f.Canonical);
        var model = Build(f, persisted, plan);
        var body = Assert.Single(model.Members, member => member.MemberKey == f.Rafter.LogicalKey);
        AssertFrame(f, body);
        AssertReconcile(baseline, model, body);
    }

    [Theory]
    [InlineData(0, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(2, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(30, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(60, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(30, LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(60, LowerEndCutMode.Perpendicular, RidgeJoinMode.Overlap)]
    [InlineData(30, LowerEndCutMode.Horizontal, RidgeJoinMode.Meet)]
    [InlineData(60, LowerEndCutMode.Horizontal, RidgeJoinMode.Overlap)]
    public void YawAnglesAndCurrentCuts_KeepTopOnPlaneAndOrthogonalSection(double yaw,
        LowerEndCutMode lower, RidgeJoinMode ridge)
    {
        var f = Solve();
        var native = Drag(f.Canonical, false, f.Canonical.LengthMm * Math.Tan(yaw * Math.PI / 180), 0);
        var grip = Accept(f.Canonical, native);
        Assert.Equal(-yaw, grip.PlanYawDeltaDegrees, 6);
        var edit = Compose(f, grip, null);
        var model = Build(f, edit, Apply(f, edit), new(lower, ridge));
        var body = Assert.Single(model.Members, member => member.MemberKey == f.Rafter.LogicalKey);
        AssertFrame(f, body);
        Assert.Equal(body.StartBoundaryRole == RoofRafterBoundaryRole.Eave && lower == LowerEndCutMode.Horizontal,
            body.HorizontalCut is not null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedLateralGrips_UseCurrentAcceptedGeometryWithoutTwist(bool start)
    {
        var f = Solve();
        var live = f.Canonical;
        RoofGeneratedMemberOverride? edit = null;
        for (var index = 0; index < 6; index++)
        {
            var native = Drag(live, start, 180, 20);
            var grip = Accept(live, native);
            Assert.Equal(native, grip.Geometry);
            Assert.False(grip.Clamped);
            Assert.NotEqual(grip.AxisBefore, grip.AxisAfter);
            edit = Persist(Compose(f, grip, edit));
            live = Apply(f, edit);
            AssertGeometry(native, live);
            AssertFrame(f, Build(f, edit, live).Members.Single(member => member.MemberKey == f.Rafter.LogicalKey));
        }
        Assert.Equal((start ? f.Canonical.Start : f.Canonical.End).X + 6 * 180,
            (start ? live.Start : live.End).X, 6);
    }

    [Theory]
    [InlineData(true, 37, -19)]
    [InlineData(false, 37, -19)]
    [InlineData(true, 5000, -2700)]
    [InlineData(false, 5000, -2700)]
    public void MoveThenFreeformGrip_PreservesMovedPlanAndRebuildsCurrentRoofPlane(bool start, double dx, double dy)
    {
        var f = Solve();
        var moved = Translate(f.Canonical, dx, dy);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(f.Canonical, moved,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, f.Rafter.LogicalKey, "K2", out var move));
        var native = Drag(moved, start, 1000, 100);
        var grip = Accept(moved, native);
        var edit = Persist(Compose(f, grip, move));
        var plan = Apply(f, edit);
        AssertGeometry(native, plan);
        AssertPoint(start ? moved.End : moved.Start, start ? plan.End : plan.Start);
        AssertFrame(f, Build(f, edit, plan).Members.Single(member => member.MemberKey == f.Rafter.LogicalKey));
    }

    [Fact]
    public void FreeformGripThenPureMove_CarriesNewBodyAndReferenceWithoutChangingMoveContract()
    {
        var f = Solve();
        var edit = Compose(f, Accept(f.Canonical, Drag(f.Canonical, true, 1000, 100)), null);
        var plan = Apply(f, edit);
        var before = Build(f, edit, plan).Members.Single(member => member.MemberKey == f.Rafter.LogicalKey);
        Assert.True(RoofGeneratedMemberOverrideMath.TryDecomposeInPlane(f.Canonical,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, new(5000, -2700, 0), out var along, out var lateral));
        var movedEdit = Persist(RoofGeneratedMemberOverrideMath.ComposeTranslation(edit, f.Rafter.LogicalKey, "K2", along, lateral)!);
        Assert.Equal(edit.PhysicalReferenceSegment, movedEdit.PhysicalReferenceSegment);
        var moved = Apply(f, movedEdit);
        AssertGeometry(Translate(plan, 5000, -2700), moved);
        var after = Build(f, movedEdit, moved).Members.Single(member => member.MemberKey == f.Rafter.LogicalKey);
        Assert.Equal(before.SolidVertices.Count, after.SolidVertices.Count);
        for (var index = 0; index < before.SolidVertices.Count; index++)
            AssertPoint(Shift(before.SolidVertices[index], 5000, -2700), after.SolidVertices[index]);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 300)]
    [InlineData(false, 300)]
    [InlineData(true, 1306)]
    [InlineData(false, 1306)]
    [InlineData(true, 100000000)]
    [InlineData(false, 100000000)]
    public void CrossingAndMinimum_ClampOnlyAlongComponent_KeepLateral(bool start, double lateral)
    {
        var before = new RoofGeneratedMemberGeometry(new(48115.221, 10292.763, 0), new(48115.221, 13441.329, 0));
        var along = (start ? 1 : -1) * (before.LengthMm + 445);
        var native = Drag(before, start, lateral, along);
        var grip = Accept(before, native);
        Assert.True(grip.Clamped);
        Assert.Equal(start ? "Start" : "End", grip.Endpoint);
        Assert.Equal(start ? before.End : before.Start, start ? grip.Geometry.End : grip.Geometry.Start);
        Assert.Equal((start ? native.Start : native.End).X, (start ? grip.Geometry.Start : grip.Geometry.End).X);
        Assert.True(grip.Geometry.End.Y > grip.Geometry.Start.Y);
        Assert.True(grip.Geometry.LengthMm >= 500 - 1e-6);
        if (lateral < 500) Assert.Equal(500, grip.Geometry.LengthMm, 6);
        else Assert.True(Math.Abs(grip.AxisAfter.X) > 0.9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApproachingMinimumAndExactCoincidence_KeepEndpointSemantics(bool start)
    {
        var before = new RoofGeneratedMemberGeometry(new(0, 0, 0), new(0, 3000, 0));
        var native = Drag(before, start, 300, start ? 2800 : -2800);
        var grip = Accept(before, native);
        Assert.Equal(500, grip.Geometry.LengthMm, 6);
        Assert.Equal(300, (start ? grip.Geometry.Start : grip.Geometry.End).X);
        var coincident = start ? before with { Start = before.End } : before with { End = before.Start };
        Assert.Equal(500, Accept(before, coincident).Geometry.LengthMm, 6);
    }

    [Fact]
    public void InvalidTwoEndpointsAndNonfiniteInput_AreRejected_ZIsNormalized()
    {
        var before = new RoofGeneratedMemberGeometry(new(0, 0, 0), new(0, 3000, 0));
        Assert.False(RoofOrdinaryFreeformGripRules.TryAccept(Drag(Drag(before, true, 100, 0), false, 100, 0),
            before, out _, out _));
        Assert.False(RoofOrdinaryFreeformGripRules.TryAccept(before, before with { Start = new(double.NaN, 0, 0) }, out _, out _));
        Assert.False(RoofOrdinaryFreeformGripRules.TryAccept(before with { End = new(0, 3000, 0.005) },
            Drag(before, true, 1000, 0), out _, out _));
        var native = before with { Start = new(1000, 0, 200) };
        Assert.Equal(new RoofPoint3D(1000, 0, 0), Accept(before, native).Geometry.Start);
    }

    [Fact]
    public void RepeatedCopiesOfFreeformGeneratedMember_KeepSourceShapeAndIndependentBodies()
    {
        var f = Solve();
        var edit = Compose(f, Accept(f.Canonical, Drag(f.Canonical, true, 1000, 700)), null);
        var plan = Apply(f, edit);
        var generated = Build(f, edit, plan);
        var source = generated.Members.Single(member => member.MemberKey == f.Rafter.LogicalKey);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(plan.Start, plan.End,
            plan.Start, plan.End, out var reference));
        var children = new List<RoofAttachedManualPhysicalInput>();
        for (var command = 0; command < 2; command++)
        {
            var dx = 5000 + command * 500;
            var dy = -2700 + command * 100;
            Assert.True(RoofOrdinaryCopyPlanRules.TryAccept(plan, Translate(plan, dx, dy), out var copy));
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(plan.Start, plan.End,
                copy.Start, copy.End, out var relative));
            var data = new RoofAttachedManualTimberData(5, "AB", "copy:" + command, RoofTimberChildRole.AttachedManual,
                source.MemberKey, relative, RoofAttachedManualOrigin.Copy, RoofAttachedManualIdentityRules.Create(), reference);
            children.Add(new(data, new(copy.Start, copy.End), 80, 125));
            Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, generated, children,
                3000, new(), null, out var model, out var reason), reason);
            Assert.Equal(generated.Members.Count + command + 1, model!.Members.Count);
            Assert.Same(source, model.Members.Single(member => member.PhysicalIdentity == source.PhysicalIdentity));
            Assert.Equal(model.Members.Count, model.Members.Select(member => member.PhysicalIdentity).Distinct().Count());
            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var body = Assert.Single(model.Members, member =>
                    member.AttachedManualIdentity == children[childIndex].Metadata.SemanticIdentity);
                for (var vertex = 0; vertex < source.SolidVertices.Count; vertex++)
                    AssertPoint(Shift(source.SolidVertices[vertex], 5000 + childIndex * 500, -2700 + childIndex * 100), body.SolidVertices[vertex]);
            }
        }
    }

    [Fact]
    public void DormantFreeformOverride_WithoutAcceptedLivePlan_UsesExistingCanonicalFallback()
    {
        var f = Solve();
        var moved = Translate(f.Canonical, 20000, -20000);
        var edit = Compose(f, Accept(moved, Drag(moved, true, 1000, 100)), null);
        var replay = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(f.Layout, [edit],
            new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>());
        Assert.Equal(RoofGeneratedMemberReplayDisposition.DormantInvalidDomain,
            replay.Items.Single(item => item.Rafter.LogicalKey == f.Rafter.LogicalKey).Disposition);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
            3000, 80, 125, new(), replay, out var model));
        var body = model!.Members.Single(member => member.MemberKey == f.Rafter.LogicalKey);
        Assert.Equal(new RoofSegment3D(f.Canonical.Start, f.Canonical.End), body.PlanAxis);
        AssertFrame(f, body);
    }

    [Fact]
    public void PhysicalReferenceCodec_PreservesNewShape_RejectsMalformedReference_AndReadsLegacy()
    {
        var f = Solve();
        var edit = Compose(f, Accept(f.Canonical, Drag(f.Canonical, true, 1000, 700)), null);
        Assert.Equal(edit, Persist(edit));
        var payload = RoofGeneratedMemberOverrideCodec.Encode([edit]);
        Assert.Equal(16, payload.Split(':').Length);
        var fields = payload.Split(':');
        fields[10] = "NaN";
        Assert.False(RoofGeneratedMemberOverrideCodec.TryDecode(string.Join(":", fields), out _, out _));
        fields = payload.Split(':');
        fields[12] = "30";
        Assert.False(RoofGeneratedMemberOverrideCodec.TryDecode(string.Join(":", fields), out _, out _));
        var legacy = edit with { PhysicalReferenceSegment = null };
        Assert.Equal(10, RoofGeneratedMemberOverrideCodec.Encode([legacy]).Split(':').Length);
        Assert.Equal(legacy, Persist(legacy));
    }

    [Fact]
    public void HostUsesFreeformAcceptance_AndMeasuresFinalPhysicalModel()
    {
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        var gripBlock = RoofUxSourceContractText.Member(manual, "if (isGrip)", "if (isFreeformRepresentable)");
        Assert.Contains("RoofOrdinaryFreeformGripRules.TryAccept", gripBlock);
        Assert.Contains("TryComposeGenerated", gripBlock);
        Assert.DoesNotContain("TryClassifyAxisConstrainedEndpointGrip", manual);
        Assert.Contains("CompleteOrdinaryGripPhysicalFrame", Read("RoofOrdinaryRafterSolidMaterializationService.cs"));
        var diag = Read("RoofGeneratedMemberManualEditDiag.cs");
        Assert.Contains("ROOF_ORDINARY_GRIP_FREEFORM", diag);
        foreach (var field in new[] { "beforeStart=", "beforeEnd=", "nativeStart=", "nativeEnd=", "acceptedStart=",
                     "acceptedEnd=", "axisBefore=", "axisAfter=", "planYawDeltaDegrees=", "roofNormal=", "physicalAxis=",
                     "topFacePlaneErrorMm=", "minimumLength=", "clamped=" }) Assert.Contains(field, diag);
        Assert.DoesNotContain("lateralRejected=", diag);
    }

    private static RoofOrdinaryFreeformGripAcceptance Accept(RoofGeneratedMemberGeometry before, RoofGeneratedMemberGeometry native)
    {
        Assert.True(RoofOrdinaryFreeformGripRules.TryAccept(before, native, out var grip, out var reason));
        Assert.True(reason is RoofGeneratedMemberManualEditReason.Accepted or RoofGeneratedMemberManualEditReason.NeitherEndpointChanged);
        return grip!;
    }
    private static RoofGeneratedMemberOverride Compose(Fixture f, RoofOrdinaryFreeformGripAcceptance grip, RoofGeneratedMemberOverride? existing)
    {
        Assert.True(RoofOrdinaryFreeformGripRules.TryComposeGenerated(f.Canonical, grip, f.Rafter.LogicalKey,
            existing, "K2", out var edit));
        // An unchanged canonical axis has no geometry override.
        return edit ?? new(f.Rafter.LogicalKey, false, 0, 0, 0, 0, 0, "K2");
    }
    private static RoofGeneratedMemberOverride Persist(RoofGeneratedMemberOverride edit)
    {
        Assert.True(RoofGeneratedMemberOverrideCodec.TryDecode(RoofGeneratedMemberOverrideCodec.Encode([edit]), out var decoded, out _));
        return Assert.Single(decoded);
    }
    private static RoofGeneratedMemberGeometry Apply(Fixture f, RoofGeneratedMemberOverride? edit)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(f.Canonical,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, edit, out var plan));
        return plan;
    }
    private static RoofAutomaticRafterPhysicalModel Build(Fixture f, RoofGeneratedMemberOverride? edit,
        RoofGeneratedMemberGeometry plan, RoofAutomaticRafterPhysicalSettings? settings = null)
    {
        var live = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [f.Rafter.LogicalKey] = plan };
        var replay = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(f.Layout, edit is null ? [] : [edit], live),
            RoofEditState.Unlocked, live);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", f.Roof.Topology, f.Faces, f.Layout,
            3000, 80, 125, settings ?? new(), replay, out var model, out var reason), reason);
        var actual = model!.Members.Single(member => member.MemberKey == f.Rafter.LogicalKey).PlanAxis;
        AssertGeometry(plan, new(actual.Start, actual.End));
        return model!;
    }
    private static void AssertFrame(Fixture f, RoofAutomaticRafterPhysicalMember member)
    {
        Assert.True(RoofOrdinaryRoofPlaneFrameRules.TryDescribe(f.Roof.Topology, member, 3000, out var frame));
        Assert.InRange(frame!.TopFacePlaneErrorMm, 0, 1e-6);
        Assert.InRange(frame.SectionOrthogonalityError, 0, 1e-8);
        Assert.InRange(frame.HeightNormalErrorMm, 0, 1e-6);
        var axis = member.PlanAxis;
        var length = Math.Sqrt(Math.Pow(frame.PhysicalAxis.X, 2) + Math.Pow(frame.PhysicalAxis.Y, 2));
        var planLength = axis.Start.DistanceTo(axis.End);
        Assert.Equal((axis.End.X - axis.Start.X) / planLength, frame.PhysicalAxis.X / length, 6);
        Assert.Equal((axis.End.Y - axis.Start.Y) / planLength, frame.PhysicalAxis.Y / length, 6);
        Assert.Equal(80, member.WidthMm);
        Assert.Equal(125, member.HeightMm);
        var prism = member.HorizontalCut?.SourcePrismVertices ?? member.StructuralCut?.SourcePrismVertices ??
            member.RidgeOverlapCut?.SourcePrismVertices ?? member.RidgeMeetCut?.SourcePrismVertices ??
            member.SolidVertices;
        Assert.True(RoofOrdinaryPhysicalPlanFrameRules.TryDescribe(axis, prism, member.WidthMm, out var correspondence, out _));
        Assert.True(correspondence!.IsFaithful);
    }
    private static void AssertReconcile(RoofAutomaticRafterPhysicalModel before, RoofAutomaticRafterPhysicalModel after,
        RoofAutomaticRafterPhysicalMember changed)
    {
        var bindings = before.Members.Select(member => new RoofOrdinaryPhysicalBinding("old:" + member.PhysicalIdentity,
            member.PhysicalIdentity)).ToArray();
        var keys = after.Members.Select(member => member.PhysicalIdentity).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(keys, [changed.PhysicalIdentity], bindings, false, out var plan));
        Assert.Equal([changed.PhysicalIdentity], plan!.RebuildKeys);
        var final = bindings.Where(binding => !plan.RemoveBodyIdentities.Contains(binding.BodyIdentity))
            .Select(binding => binding.SemanticKey).Concat(plan.RebuildKeys).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(keys, final));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(final, keys));
    }
    private static Fixture Solve()
    {
        var footprint = RoofFootprintValidator.Validate(new([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint.Footprint!, new(30), RoofKind.Hip)).Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        var rafter = layout.Rafters.First(item => item.PlanStart.Y == 0 && item.PlanStart.X > 4000 && item.PlanStart.X < 6000);
        return new(roof, faces, layout, rafter, RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0));
    }
    private static RoofGeneratedMemberGeometry Drag(RoofGeneratedMemberGeometry geometry, bool start, double dx, double dy) =>
        start ? geometry with { Start = Shift(geometry.Start, dx, dy) } : geometry with { End = Shift(geometry.End, dx, dy) };
    private static RoofPoint3D Shift(RoofPoint3D point, double dx, double dy) => new(point.X + dx, point.Y + dy, point.Z);
    private static RoofGeneratedMemberGeometry Translate(RoofGeneratedMemberGeometry geometry, double dx, double dy) =>
        new(Shift(geometry.Start, dx, dy), Shift(geometry.End, dx, dy));
    private static void AssertGeometry(RoofGeneratedMemberGeometry expected, RoofGeneratedMemberGeometry actual)
    { AssertPoint(expected.Start, actual.Start); AssertPoint(expected.End, actual.End); }
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) => Assert.True(expected.DistanceTo(actual) < 1e-6);
    private static string Read(string file) => RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", file);
}
