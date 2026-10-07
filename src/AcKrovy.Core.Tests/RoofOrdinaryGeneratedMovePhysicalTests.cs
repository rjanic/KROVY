using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryGeneratedMovePhysicalTests
{
    [Theory]
    [InlineData(5000, -2700, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(37, -19, LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(5000, -2700, LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(37, -19, LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(5000, -2700, LowerEndCutMode.Horizontal, RidgeJoinMode.Overlap)]
    [InlineData(37, -19, LowerEndCutMode.Horizontal, RidgeJoinMode.Overlap)]
    public void PersistedMove_RebuildsOneBodyAsRigidXYTranslation(double dx, double dy,
        LowerEndCutMode cut, RidgeJoinMode ridge)
    {
        var (roof, faces, layout) = Solve();
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 2);
        var rafter = Assert.Single(layout.Rafters, item => item.LogicalKey == key);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(Translate(canonical.Start, dx, dy), Translate(canonical.End, dx, dy));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(canonical, moved,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, key, "K2", out var edit));
        Assert.Equal(key, edit!.Key);
        Assert.False(edit.Suppressed);
        Assert.Equal("K2", edit.ReservedElementId);
        Assert.Equal(dy, edit.AlongMm, 6);
        Assert.Equal(-dx, edit.LateralMm, 6);
        var persisted = JsonSerializer.Deserialize<RoofGeneratedMemberOverride>(JsonSerializer.Serialize(edit))!;
        Assert.Equal(edit, persisted);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, persisted, out var applied));
        Assert.Equal(moved, applied);
        var settings = new RoofAutomaticRafterPhysicalSettings(cut, ridge);
        var baseline = Build(roof, faces, layout, settings,
            RoofGeneratedMemberReplayPlanner.Create(layout, 0, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, null));
        var replay = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout,
            new[] { persisted }, new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [key] = moved });
        var rebuilt = Build(roof, faces, layout, settings, replay);
        var before = Assert.Single(baseline.Members, item => item.MemberKey == key);
        var after = Assert.Single(rebuilt.Members, item => item.MemberKey == key);
        Assert.Equal("Rafter:Face0:2", after.PhysicalIdentity);
        Assert.Null(after.AttachedManualIdentity);
        Assert.Equal(new RoofSegment3D(moved.Start, moved.End), after.PlanAxis);
        Assert.Equal(0, after.PlanAxis.Start.Z);
        Assert.Equal(0, after.PlanAxis.End.Z);
        Assert.Equal(before.SolidVertices.Count, after.SolidVertices.Count);
        for (var i = 0; i < before.SolidVertices.Count; i++)
            AssertPoint(Translate(before.SolidVertices[i], dx, dy), after.SolidVertices[i]);
        Assert.Equal(before.PhysicalLengthMm, after.PhysicalLengthMm);
        Assert.Equal(before.StartBoundaryRole, after.StartBoundaryRole);
        Assert.Equal(before.EndBoundaryRole, after.EndBoundaryRole);
        if (before.HorizontalCut is { } horizontal)
        {
            Assert.Equal(horizontal.EaveElevationMm, after.HorizontalCut!.EaveElevationMm);
            AssertPoints(horizontal.SourcePrismVertices, after.HorizontalCut.SourcePrismVertices, dx, dy);
            AssertPoints(horizontal.CutFaceVertices, after.HorizontalCut.CutFaceVertices, dx, dy);
        }
        if (before.StructuralCut is { } structural)
        {
            AssertPoint(Translate(structural.PlanePoint, dx, dy), after.StructuralCut!.PlanePoint);
            Assert.Equal(structural.PlaneNormal, after.StructuralCut.PlaneNormal);
            AssertPoints(structural.SourcePrismVertices, after.StructuralCut.SourcePrismVertices, dx, dy);
        }
        Assert.NotNull(before.StructuralCut);
        if (before.RidgeOverlapCut is { } overlap)
        {
            AssertPoint(Translate(overlap.PlanePoint, dx, dy), after.RidgeOverlapCut!.PlanePoint);
            AssertPoints(overlap.SourcePrismVertices, after.RidgeOverlapCut.SourcePrismVertices, dx, dy);
        }
        var untouched = baseline.Members.Where(item => item.MemberKey != key).ToArray();
        foreach (var member in untouched)
            Assert.Equal(member.SolidVertices, rebuilt.Members.Single(item => item.MemberKey == member.MemberKey).SolidVertices);
        var keys = rebuilt.Members.Select(item => item.PhysicalIdentity).ToArray();
        var bindings = baseline.Members.Select(item => new RoofOrdinaryPhysicalBinding("old-" + item.PhysicalIdentity,
            item.PhysicalIdentity)).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(keys, new[] { after.PhysicalIdentity }, bindings,
            false, out var reconcile));
        Assert.Equal(new[] { "old-Rafter:Face0:2" }, reconcile!.RemoveBodyIdentities);
        Assert.Equal(new[] { "Rafter:Face0:2" }, reconcile.RebuildKeys);
        var finalKeys = bindings.Where(item => !reconcile.RemoveBodyIdentities.Contains(item.BodyIdentity))
            .Select(item => item.SemanticKey).Concat(reconcile.RebuildKeys).ToArray();
        Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(keys, finalKeys));
        Assert.Single(finalKeys, item => item == "Rafter:Face0:2");
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(finalKeys, keys));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("canonical")]
    [InlineData("mismatch")]
    [InlineData("nonfinite")]
    public void ExistingPhysicalReplay_DoesNotReviveDormantOverrideWithoutMatchingLivePlan(string caseName)
    {
        var (roof, faces, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(Translate(canonical.Start, 5000, -2700), Translate(canonical.End, 5000, -2700));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(canonical, moved,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, rafter.LogicalKey, "K2", out var edit));
        var live = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>();
        if (caseName == "canonical") live[rafter.LogicalKey] = canonical;
        if (caseName == "mismatch") live[rafter.LogicalKey] = moved with { End = Translate(moved.End, 10, 0) };
        if (caseName == "nonfinite") live[rafter.LogicalKey] = moved with { Start = new(double.NaN, moved.Start.Y, 0) };
        var standard = RoofGeneratedMemberReplayPlanner.Create(layout, 0,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, new[] { edit! });
        var physical = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout, new[] { edit! }, live);
        Assert.Equal(1, standard.DormantInvalidDomainCount);
        Assert.Equal(standard.Items, physical.Items);
        Assert.Equal(standard.DormantInvalidDomainCount, physical.DormantInvalidDomainCount);
        Assert.Equal(standard.GeometryReplayCount, physical.GeometryReplayCount);
        var model = Build(roof, faces, layout, new(), physical);
        Assert.Equal(new RoofSegment3D(canonical.Start, canonical.End), model.Members.Single(item => item.MemberKey == rafter.LogicalKey).PlanAxis);
    }

    [Fact]
    public void ExistingPhysicalReplay_KeepsSuppressionAndResizeDormancyUnchanged()
    {
        var (_, _, layout) = Solve();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(Translate(canonical.Start, 5000, -2700), Translate(canonical.End, 5000, -2700));
        var edit = new RoofGeneratedMemberOverride(rafter.LogicalKey, false, -2700, -5000, 0, 0, 0, "K2");
        var before = JsonSerializer.Serialize(edit);
        var live = new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [rafter.LogicalKey] = moved };
        var accepted = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout, new[] { edit }, live);
        Assert.Equal(1, accepted.GeometryReplayCount);
        Assert.Equal(0, accepted.DormantInvalidDomainCount);
        var resize = RoofGeneratedMemberReplayPlanner.Create(layout, 0,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, new[] { edit });
        Assert.Equal(1, resize.DormantInvalidDomainCount);
        Assert.Equal(before, JsonSerializer.Serialize(edit));
        var suppressed = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout,
            new[] { RoofGeneratedMemberOverride.Suppress(rafter.LogicalKey, "K2") }, live);
        Assert.Equal(1, suppressed.SuppressedCount);
        Assert.Null(suppressed.Items.Single(item => item.Rafter.LogicalKey == rafter.LogicalKey).Geometry);
    }

    [Fact]
    public void HostCoordinates_ExistingLocalSignsReplayExactAcceptedMove()
    {
        var original = new RoofGeneratedMemberGeometry(new(38201.85656854813, 10951.930768028607, 0),
            new(38201.85656854813, 12801.930768028607, 0));
        var moved = new RoofGeneratedMemberGeometry(Translate(original.Start, 5000, -2700), Translate(original.End, 5000, -2700));
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 2);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(original, moved,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, key, "K2", out var edit));
        Assert.Equal(-2700, edit!.AlongMm);
        Assert.Equal(-5000, edit.LateralMm);
        Assert.Equal(0, edit.RotationRadians);
        Assert.Equal(0, edit.StartOffsetMm);
        Assert.Equal(0, edit.EndOffsetMm);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(original,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, edit, out var replayed));
        Assert.Equal(moved, replayed);
    }

    [Theory]
    [InlineData(5000, -2700)]
    [InlineData(37, -19)]
    public void MovedRidgeOverlap_TranslatesClipPlaneAndConstructionPrism(double dx, double dy)
    {
        var (roof, faces, layout) = Solve();
        var settings = new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Vertical, RidgeJoinMode.Overlap);
        var baseline = Build(roof, faces, layout, settings,
            RoofGeneratedMemberReplayPlanner.Create(layout, 0, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, null));
        var original = baseline.Members.First(member => member.RidgeOverlapCut is not null);
        var canonical = new RoofGeneratedMemberGeometry(original.PlanAxis.Start, original.PlanAxis.End);
        var moved = new RoofGeneratedMemberGeometry(Translate(canonical.Start, dx, dy), Translate(canonical.End, dx, dy));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(canonical, moved,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, original.MemberKey, "K2", out var edit));
        var replay = RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(layout, new[] { edit! },
            new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry> { [original.MemberKey] = moved });
        var member = Build(roof, faces, layout, settings, replay).Members.Single(item => item.MemberKey == original.MemberKey);
        AssertPoints(original.SolidVertices, member.SolidVertices, dx, dy);
        AssertPoint(Translate(original.RidgeOverlapCut!.PlanePoint, dx, dy), member.RidgeOverlapCut!.PlanePoint);
        Assert.Equal(original.RidgeOverlapCut.RetainedNormal, member.RidgeOverlapCut.RetainedNormal);
        AssertPoints(original.RidgeOverlapCut.SourcePrismVertices, member.RidgeOverlapCut.SourcePrismVertices, dx, dy);
        AssertPoints(original.RidgeOverlapCut.CutFaceVertices, member.RidgeOverlapCut.CutFaceVertices, dx, dy);
    }

    [Fact]
    public void Adapter_RebuildReadsLiveExactKeyPlanAndPersistedOverrideAfterAcceptance()
    {
        var adapter = RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryRafterSolidMaterializationService.cs");
        var build = RoofUxSourceContractText.Member(adapter, "public static bool TryBuildExistingModelInTransaction(",
            "public static bool TryReconcileModifiedMembersInTransaction(");
        Assert.Contains("RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers", build);
        Assert.Contains("RoofGeneratedMemberKey.From(generated)", build);
        Assert.Contains("planLine.StartPoint", build);
        Assert.Contains("RoofDefinitionStore.Read(owner).Data?.Overrides", build);
        var accept = RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
        var write = accept.IndexOf("RoofDefinitionStore.Write(owner, transaction, updated)", StringComparison.Ordinal);
        var reconcile = accept.IndexOf(".TryReconcileModifiedMembersInTransaction(", write, StringComparison.Ordinal);
        Assert.True(write >= 0 && reconcile > write);
    }

    private static RoofAutomaticRafterPhysicalModel Build(HipRoofGeometry roof, RoofFaceRafterLayout faces,
        RoofRafterLayout layout, RoofAutomaticRafterPhysicalSettings settings, RoofGeneratedMemberReplayPlan replay)
    {
        var sources = roof.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                roof.Topology.Segment(item.edge), 120)).ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("2912", roof.Topology, faces, layout,
            0, 80, 125, settings, replay, sources, out var model, out var failure), failure);
        return model!;
    }
    private static (HipRoofGeometry, RoofFaceRafterLayout, RoofRafterLayout) Solve()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(33991.69391084029, 10951.930768028607), new(43991.69391084029, 10951.930768028607),
             new(43991.69391084029, 16951.930768028607), new(33991.69391084029, 16951.930768028607)], true));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(footprint.Footprint!, new RoofParameters(30), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var roof = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        return (roof, faces, layout);
    }
    private static RoofPoint3D Translate(RoofPoint3D point, double dx, double dy) => new(point.X + dx, point.Y + dy, point.Z);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) => Assert.InRange(expected.DistanceTo(actual), 0, 1e-6);
    private static void AssertPoints(IReadOnlyList<RoofPoint3D> before, IReadOnlyList<RoofPoint3D> after, double dx, double dy)
    {
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++) AssertPoint(Translate(before[i], dx, dy), after[i]);
    }
}
