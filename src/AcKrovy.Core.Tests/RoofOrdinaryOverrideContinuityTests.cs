using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryOverrideContinuityTests
{
    private static readonly RoofPoint3D Normal = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;

    [Theory]
    [InlineData("MOVE", 0, LowerEndCutMode.Vertical)]
    [InlineData("STRETCH", 600, LowerEndCutMode.Vertical)]
    [InlineData("GRIP_STRETCH", 600, LowerEndCutMode.Perpendicular)]
    [InlineData("STRETCH", 900, LowerEndCutMode.Horizontal)]
    [InlineData("TRIM", -300, LowerEndCutMode.Vertical)]
    [InlineData("EXTEND", 600, LowerEndCutMode.Horizontal)]
    public void AcceptedCumulativeGeometry_RebuildsShapeThenCarriesPriorMove(
        string command, double extension, LowerEndCutMode cut)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand(command));
        var (roof, faces, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(Shift(canonical.Start, 5000, -2700), Shift(canonical.End, 5000, -2700));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(canonical, moved, Normal, rafter.LogicalKey, "K2", out var classifiedMove));
        var move = classifiedMove!;
        var observed = moved with { End = Shift(moved.End, 0, extension) };
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(moved, observed, Normal,
            out var startDelta, out var endDelta, out var accepted, out _));
        var cumulative = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(move, rafter.LogicalKey, "K2", startDelta, endDelta)!;
        // Persisted accepted state is the input to reconcile, including the earlier MOVE.
        cumulative = JsonSerializer.Deserialize<RoofGeneratedMemberOverride>(JsonSerializer.Serialize(cumulative))!;
        var plan = Apply(canonical, cumulative);
        Assert.Equal(accepted, plan);
        var settings = new RoofAutomaticRafterPhysicalSettings(cut, RidgeJoinMode.Meet);
        var replay = Accepted(layout, cumulative,
            new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [rafter.LogicalKey] = plan });
        Assert.Equal(0, replay.DormantCount);
        Assert.Equal(1, replay.GeometryReplayCount);
        var model = Build(roof, faces, layout, settings, replay);
        var member = Assert.Single(model.Members, item => item.MemberKey == rafter.LogicalKey);
        var localEdit = cumulative with { AlongMm = 0, LateralMm = 0 };
        var local = Build(roof, faces, layout, settings, Accepted(layout, localEdit));
        var reference = local.Members.Single(item => item.MemberKey == rafter.LogicalKey);
        Assert.Equal(new RoofSegment3D(plan.Start, plan.End), member.PlanAxis);
        Assert.Equal("Rafter:Face0:2", member.PhysicalIdentity);
        Assert.Null(member.AttachedManualIdentity);
        Assert.Equal(0, plan.Start.Z);
        Assert.Equal(0, plan.End.Z);
        Assert.Equal(-2700, cumulative.AlongMm, 6);
        Assert.Equal(-5000, cumulative.LateralMm, 6);
        Assert.Equal(extension, cumulative.EndOffsetMm, 6);
        Assert.Equal("K2", replay.Items.Single(item => item.Rafter.LogicalKey == rafter.LogicalKey).Override!.ReservedElementId);
        Assert.Equal(reference.PhysicalLengthMm, member.PhysicalLengthMm, 6);
        Assert.Equal(reference.PitchDegrees, member.PitchDegrees, 6);
        Assert.Equal(80, member.WidthMm);
        Assert.Equal(125, member.HeightMm);
        Assert.Equal(reference.StartBoundaryRole, member.StartBoundaryRole);
        Assert.Equal(reference.EndBoundaryRole, member.EndBoundaryRole);
        Assert.Equal(reference.SolidVertices.Count, member.SolidVertices.Count);
        for (var i = 0; i < reference.SolidVertices.Count; i++)
            AssertPoint(Shift(reference.SolidVertices[i], 5000, -2700), member.SolidVertices[i]);
        if (reference.HorizontalCut is { } horizontal)
        {
            Assert.Equal(horizontal.EaveElevationMm, member.HorizontalCut!.EaveElevationMm);
            AssertTranslated(horizontal.CutFaceVertices, member.HorizontalCut.CutFaceVertices);
        }
        if (reference.StructuralCut is { } structural)
        {
            AssertPoint(Shift(structural.PlanePoint, 5000, -2700), member.StructuralCut!.PlanePoint);
            Assert.Equal(structural.PlaneNormal, member.StructuralCut.PlaneNormal);
        }
        var baseline = Build(roof, faces, layout, settings, Accepted(layout, move));
        if (extension != 0)
            Assert.True(Math.Abs(member.PhysicalLengthMm - baseline.Members.Single(item => item.MemberKey == rafter.LogicalKey).PhysicalLengthMm) > 100);
        foreach (var other in model.Members.Where(item => item.MemberKey != rafter.LogicalKey))
            Assert.Equal(local.Members.Single(item => item.MemberKey == other.MemberKey).SolidVertices, other.SolidVertices);
        AssertCanonicalReplacement(model, member);
    }

    [Fact]
    public void RepeatedEndpointEdits_KeepTranslationAndEarlierOffset()
    {
        var (roof, faces, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var edit = new RoofGeneratedMemberOverride(rafter.LogicalKey, false, -2700, -5000, 0, 100, 600, "K2");
        var first = Apply(canonical, edit);
        var observed = first with { End = Shift(first.End, 0, 300) };
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(first, observed, Normal,
            out var startDelta, out var endDelta, out var accepted, out _));
        var second = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(edit, rafter.LogicalKey, "K2", startDelta, endDelta)!;
        var final = Apply(canonical, second);
        Assert.Equal(accepted, final);
        Assert.Equal(first.Start, final.Start);
        Assert.Equal(300, final.LengthMm - first.LengthMm, 6);
        var replay = Accepted(layout, second, new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [rafter.LogicalKey] = final });
        Assert.Equal(final, replay.Items.Single(item => item.Rafter.LogicalKey == rafter.LogicalKey).Geometry);
        Assert.Equal(second, replay.Items.Single(item => item.Rafter.LogicalKey == rafter.LogicalKey).Override);
        var model = Build(roof, faces, layout, new(), replay);
        var member = Assert.Single(model.Members, item => item.MemberKey == rafter.LogicalKey);
        var local = Build(roof, faces, layout, new(), Accepted(layout, second with { AlongMm = 0, LateralMm = 0 }))
            .Members.Single(item => item.MemberKey == rafter.LogicalKey);
        Assert.Equal(new RoofSegment3D(final.Start, final.End), member.PlanAxis);
        AssertTranslated(local.SolidVertices, member.SolidVertices);
        AssertCanonicalReplacement(model, member);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 100, 600)]
    [InlineData(0.1, 100, 600)]
    public void Resize_ResolvedAcceptedOutsideOverride_ReplaysFullState(double rotation, double start, double end)
    {
        var (_, _, oldLayout) = Solve();
        var key = oldLayout.Rafters.Single(item => item.StationIndex == 2).LogicalKey;
        var edit = new RoofGeneratedMemberOverride(key, false, -2700, -5000, rotation, start, end, "K2");
        var persisted = JsonSerializer.Serialize(edit);
        var (roof, faces, layout) = Solve(12000, 6000);
        var rafter = Assert.Single(layout.Rafters, item => item.LogicalKey == key);
        var expected = Apply(RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0), edit);
        Assert.False(RoofGeneratedMemberDomainRules.OverlapsBoundedPlane(layout, rafter, expected));
        var replay = Accepted(layout, edit);
        Assert.Equal(0, replay.DormantInvalidDomainCount);
        Assert.Equal(1, replay.ResolvedOverrideCount);
        Assert.Equal(1, replay.GeometryReplayCount);
        Assert.Equal(expected, replay.Items.Single(item => item.Rafter.LogicalKey == key).Geometry);
        Assert.Equal(persisted, JsonSerializer.Serialize(edit));
        var model = Build(roof, faces, layout, new(), replay);
        var member = model.Members.Single(item => item.MemberKey == key);
        Assert.Equal(new RoofSegment3D(expected.Start, expected.End), member.PlanAxis);
        var local = Build(roof, faces, layout, new(), Accepted(layout, edit with { AlongMm = 0, LateralMm = 0 }))
            .Members.Single(item => item.MemberKey == key);
        AssertTranslated(local.SolidVertices, member.SolidVertices);
        AssertCanonicalReplacement(model, member);
    }

    [Fact]
    public void Resize_MissingExactKey_RemainsStoredAndNeverRebinds()
    {
        var (_, _, original) = Solve();
        var key = original.Rafters.Last().LogicalKey;
        var edit = new RoofGeneratedMemberOverride(key, false, -2700, -5000, 0, 100, 600, "K2");
        var (_, _, small) = Solve(3000, 2000);
        Assert.DoesNotContain(small.Rafters, item => item.LogicalKey == key);
        var replay = Accepted(small, edit);
        Assert.Equal(1, replay.DormantMissingKeyCount);
        Assert.Equal(0, replay.GeometryReplayCount);
        Assert.All(replay.Items, item => Assert.Null(item.Override));
        Assert.Equal(1, replay.StoredOverrideCount);
        Assert.Equal(small.Rafters.Count, replay.Items.Select(item => item.Rafter.LogicalKey).Distinct().Count());
        Assert.Equal(1, Accepted(original, edit).GeometryReplayCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("canonical")]
    [InlineData("mismatch")]
    [InlineData("nonfinite")]
    public void Reconcile_MismatchingAcceptedPlan_FailsWithoutMaterializingCanonicalBody(string scenario)
    {
        var (_, _, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var edit = new RoofGeneratedMemberOverride(rafter.LogicalKey, false, -2700, -5000, 0, 100, 600, "K2");
        var live = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>();
        if (scenario == "canonical") live[rafter.LogicalKey] = canonical;
        if (scenario == "mismatch") live[rafter.LogicalKey] = Apply(canonical, edit) with { End = canonical.End };
        if (scenario == "nonfinite") live[rafter.LogicalKey] = new(new(double.NaN, 0, 0), canonical.End);
        var replay = Accepted(layout, edit, live);
        Assert.False(replay.IsValid);
        Assert.Equal("accepted-override-live-plan-mismatch", replay.FailureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    public void LockedPolicy_LeavesExistingReplayAndMetadataUnchanged(double endOffset)
    {
        var (_, _, layout) = Solve();
        var key = layout.Rafters.Single(item => item.StationIndex == 2).LogicalKey;
        var edit = new RoofGeneratedMemberOverride(key, false, -2700, -5000, 0, 0, endOffset, "K2");
        var original = RoofGeneratedMemberReplayPlanner.Create(layout, 0, Normal, new[] { edit });
        Assert.Same(original, RoofAcceptedOrdinaryOverrideReplayRules.Apply(original, RoofEditState.Locked));
        Assert.Equal(1, original.DormantInvalidDomainCount);
        Assert.Equal(edit, original.Items.Single(item => item.Rafter.LogicalKey == key).Override);
    }

    [Fact]
    public void Suppression_StaysSuppressedAndHasNoPhysicalMember()
    {
        var (roof, faces, layout) = Solve();
        var key = layout.Rafters.Single(item => item.StationIndex == 2).LogicalKey;
        var replay = Accepted(layout, RoofGeneratedMemberOverride.Suppress(key, "K2"));
        Assert.Equal(1, replay.SuppressedCount);
        Assert.Equal(0, replay.GeometryReplayCount);
        Assert.DoesNotContain(Build(roof, faces, layout, new(), replay).Members, member => member.MemberKey == key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    [InlineData(900)]
    public void ReplayedPlan_DrivesCanonicalAnnotationPlacementAndIdempotentOwnership(double endOffset)
    {
        var (_, _, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var edit = new RoofGeneratedMemberOverride(rafter.LogicalKey, false, -2700, -5000, 0, 0, endOffset, "K2");
        var replay = Accepted(layout, edit);
        var geometry = replay.Items.Single(item => item.Rafter.LogicalKey == rafter.LogicalKey).Geometry!.Value;
        var local = Apply(RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0), edit with { AlongMm = 0, LateralMm = 0 });
        var anchor = TimberSlopeArrowCalculator.CalculatePosition(geometry.Start.X, geometry.Start.Y, geometry.End.X, geometry.End.Y);
        var localAnchor = TimberSlopeArrowCalculator.CalculatePosition(local.Start.X, local.Start.Y, local.End.X, local.End.Y);
        Assert.Equal(localAnchor.X + 5000, anchor.X, 6);
        Assert.Equal(localAnchor.Y - 2700, anchor.Y, 6);
        var data = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K2", SlopeDegrees = 30, AnnotationMode = TimberAnnotationMode.DimensionsLeader,
        };
        var annotationPlan = TimberAnnotationRefreshPlanner.Create(data);
        Assert.True(annotationPlan.EnsureLabel && annotationPlan.ShouldSlopeArrowExist && annotationPlan.ShouldSlopeAngleTextExist);
        var roles = TimberCompositeAnnotationLifecycleRules.RequiredRoles(data.AnnotationMode, data.ItemNumberLeaderStyle);
        Assert.Single(roles);
        var label = new TimberElementLabelCandidate
        {
            LabelKey = "dimension", SourceHandle = "2940", ElementId = "K2", ComponentRole = roles.Single(),
        };
        var duplicate = label with { LabelKey = "stale-dimension" };
        var deleted = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(new[] { label, duplicate }, new[] { "2940" });
        Assert.Equal(new[] { "stale-dimension" }, deleted);
        for (var pass = 0; pass < 2; pass++)
        {
            var selection = TimberElementLabelMatchRules.SelectLabelForUpsert("2940", "K2", null,
                new[] { label }, currentElementOwnerCount: 1, previousElementOwnerCount: 0, allowElementIdFallback: false);
            Assert.Equal("dimension", selection.LabelKeyToUpdate);
            Assert.Empty(selection.LabelKeysToDelete);
        }
        Assert.Empty(TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(new[] { label }, new[] { "2940" }));
    }

    private static RoofGeneratedMemberReplayPlan Accepted(RoofRafterLayout layout, RoofGeneratedMemberOverride edit,
        IReadOnlyDictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>? live = null) =>
        RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            live is null ? RoofGeneratedMemberReplayPlanner.Create(layout, 0, Normal, new[] { edit }) :
                RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout, new[] { edit }, live),
            RoofEditState.Unlocked, live);

    private static RoofGeneratedMemberGeometry Apply(RoofGeneratedMemberGeometry canonical, RoofGeneratedMemberOverride edit)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, Normal, edit, out var geometry));
        return geometry;
    }
    private static void AssertCanonicalReplacement(RoofAutomaticRafterPhysicalModel model, RoofAutomaticRafterPhysicalMember member)
    {
        var keys = model.Members.Select(item => item.PhysicalIdentity).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(keys, new[] { member.PhysicalIdentity },
            keys.Select(key => new RoofOrdinaryPhysicalBinding("old-" + key, key)).ToArray(), false, out var plan));
        Assert.Equal(new[] { "old-" + member.PhysicalIdentity }, plan!.RemoveBodyIdentities);
        var final = keys.Where(key => key != member.PhysicalIdentity).Concat(plan.RebuildKeys).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(keys, final));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(final, keys));
        Assert.Single(final, key => key == member.PhysicalIdentity);
    }
    private static RoofAutomaticRafterPhysicalModel Build(HipRoofGeometry roof, RoofFaceRafterLayout faces,
        RoofRafterLayout layout, RoofAutomaticRafterPhysicalSettings settings, RoofGeneratedMemberReplayPlan replay)
    {
        var sources = roof.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                roof.Topology.Segment(item.edge), 120)).ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("2912", roof.Topology, faces, layout, 0, 80, 125,
            settings, replay, sources, out var model, out var failure), failure);
        return model!;
    }
    private static (HipRoofGeometry, RoofFaceRafterLayout, RoofRafterLayout) Solve(double width = 10000, double depth = 6000)
    {
        const double x = 33991.69391084029, y = 10951.930768028607;
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(x, y), new(x + width, y), new(x + width, y + depth), new(x, y + depth)], true));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(footprint.Footprint!, new RoofParameters(30), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var roof = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        return (roof, faces, layout);
    }
    private static RoofPoint3D Shift(RoofPoint3D p, double dx, double dy) => new(p.X + dx, p.Y + dy, p.Z);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) => Assert.InRange(expected.DistanceTo(actual), 0, 1e-6);
    private static void AssertTranslated(IReadOnlyList<RoofPoint3D> before, IReadOnlyList<RoofPoint3D> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++) AssertPoint(Shift(before[i], 5000, -2700), after[i]);
    }
}
