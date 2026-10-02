using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralAttachedManualFoundationTests
{
    [Fact]
    public void Create_MintsNormalizedIdentityAndPhysicalKey()
    {
        var key = new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 1, 4);
        var identity = RoofStructuralAttachedManualIdentityRules.Create();
        Assert.True(RoofStructuralAttachedManualIdentityRules.TryNormalize(identity, out var normalized));
        Assert.Equal(identity, normalized);
        var created = RoofStructuralAttachedManualDataRules.Create(
            "ABCD", identity, key, RoofStructuralAttachedManualCreationKind.Copy,
            120d, RoofStructuralHeightMode.Automatic, null);
        Assert.True(created.IsValid);
        Assert.Equal(key, created.Data!.SourceLogicalKey);
        Assert.Equal(
            "ManualStructural:" + identity,
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(created.Data.ManualIdentity));
        Assert.True(RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(identity)));
        Assert.False(RoofStructuralAttachedManualDataRules.IsManualPhysicalKey("Hip|1|4"));
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void RoundtripValidation_RejectsMalformedPayload(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 2, 5);
        var identity = RoofStructuralAttachedManualIdentityRules.Create();
        Assert.True(RoofStructuralAttachedManualDataRules.Create(
            "1A2B", identity, key, RoofStructuralAttachedManualCreationKind.Copy,
            120d, RoofStructuralHeightMode.Automatic, null).IsValid);
        Assert.False(RoofStructuralAttachedManualDataRules.Create(
            "1A2B", "not-a-guid", key, RoofStructuralAttachedManualCreationKind.Copy,
            120d, RoofStructuralHeightMode.Automatic, null).IsValid);
        Assert.False(RoofStructuralAttachedManualDataRules.Create(
            "1A2B", identity, key, RoofStructuralAttachedManualCreationKind.Copy,
            0d, RoofStructuralHeightMode.Automatic, null).IsValid);
        Assert.False(RoofStructuralAttachedManualDataRules.Create(
            "1A2B", identity, key, RoofStructuralAttachedManualCreationKind.Copy,
            120d, RoofStructuralHeightMode.Explicit, null).IsValid);
    }

    [Fact]
    public void Allocator_ProducesUniqueIdentities()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 100; i++)
            Assert.True(set.Add(RoofStructuralAttachedManualIdentityRules.Create()));
    }

    [Fact]
    public void ManualPlace_UsesSameFoldConstrainedPathAsGeneratedAbsolutePlan()
    {
        // Provenance fold + absolute Plan of a COPY translation must Place without collapse.
        var fixturePitch = 30d;
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(fixturePitch), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var topology = geometry.Topology;
        var layoutResult = RoofFaceRafterLayoutService.Create(topology, 500d);
        Assert.True(layoutResult.IsValid);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layoutResult.Layout!, 80d, out var generated));
        var sources = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                RoofRafterBoundaryRole.Hip, topology.Segment(item.edge), 120d)).ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "roof", topology, layoutResult.Layout!, generated, 3000d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Vertical), null, sources,
            out var ordinary, out var reason), reason);
        var selected = topology.Edges.Select((edge, index) => (edge, index))
            .First(item => item.edge.Kind == RoofTopologyEdgeKind.Hip &&
                ordinary!.Members.Any(member =>
                    member.StructuralCut?.TopologyEdgeIndex == item.index));
        var faceIds = selected.edge.FaceIndices
            .Select(index => topology.Faces[index].SourceEdgeIndex + 1)
            .OrderBy(id => id).ToArray();
        var key = new RoofStructuralLogicalKey(RoofStructuralRole.Hip, faceIds[0], faceIds[1]);
        var resolved = new ResolvedRoofStructuralEdge(
            key, selected.index, topology.Segment(selected.edge),
            IsPhysicalFoldTimberEligible: true);
        var request = new RoofStructuralRafterPolyhedronRequest(topology, resolved,
            3000d, 120d, RoofStructuralHeightMode.Automatic, null, ordinary!.Members);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(request, out var body, out reason), reason);
        var plan = new RoofSegment3D(
            new(body!.Geometry.UpperAxis.Start.X, body.Geometry.UpperAxis.Start.Y, 0),
            new(body.Geometry.UpperAxis.End.X, body.Geometry.UpperAxis.End.Y, 0));
        const double dx = 1000d;
        var copied = new RoofSegment3D(
            new(plan.Start.X + dx, plan.Start.Y, 0),
            new(plan.End.X + dx, plan.End.Y, 0));
        Assert.Equal(RoofStructuralPlanEditClass.OffsetRigid,
            RoofStructuralEditRules.ClassifyPlanGeometry(plan, copied));
        var edit = new RoofStructuralMemberEdit(key, 0, 0, false,
            copied.Start.X, copied.Start.Y, copied.End.X, copied.End.Y);
        var placed = RoofStructuralEditRules.Place(body, plan, edit);
        Assert.Equal(key, placed.StructuralKey);
        Assert.InRange(placed.Geometry.UpperAxis.Start.X,
            body.Geometry.UpperAxis.Start.X + dx - 1d,
            body.Geometry.UpperAxis.Start.X + dx + 1d);
        Assert.True(placed.Geometry.UpperAxis.Start.Z > 500d);
        Assert.Equal(body.Geometry.WidthMm, placed.Geometry.WidthMm, 5);
    }

    [Fact]
    public void RigidCopy_TranslatesSnapshottedFrameWithoutOrdinaryModel()
    {
        var source = new RoofStructuralManualPlacement(
            0, 0, 1200, 5000, 0, 2800,
            0, 1, 0,
            0, 0, 1,
            160);
        var sourcePlan = new RoofSegment3D(new(0, 0, 0), new(5000, 0, 0));
        var copiedPlan = new RoofSegment3D(new(1000, 0, 0), new(6000, 0, 0));
        Assert.True(RoofStructuralManualPlacementRules.TryMatchRigidPlanCopy(
            sourcePlan, copiedPlan, out var dx, out var dy));
        Assert.Equal(1000d, dx, 3);
        Assert.Equal(0d, dy, 3);
        var translated = RoofStructuralManualPlacementRules.Translate(source, dx, dy, 0);
        Assert.Equal(1200d, translated.AxisStartZ, 3);
        Assert.Equal(2800d, translated.AxisEndZ, 3);
        Assert.Equal(1000d, translated.AxisStartX, 3);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            RoofStructuralRole.Hip, 120d, translated, out var body, out var reason), reason);
        Assert.NotNull(body);
        Assert.Equal(120d, body!.Geometry.WidthMm, 3);
        Assert.Equal(160d, body.Geometry.PhysicalVerticalHeightMm, 3);
        Assert.InRange(body.Geometry.UpperAxis.Start.X, 999d, 1001d);
        Assert.InRange(body.Geometry.UpperAxis.Start.Z, 1199d, 1201d);
        Assert.Equal(RoofStructuralRole.Hip, body.Geometry.Role);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Copy)]
    public void Create_SnapshotsSectionAndPreservesSourceRole(
        RoofStructuralRole role,
        RoofStructuralAttachedManualCreationKind creationKind)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var identity = RoofStructuralAttachedManualIdentityRules.Create();
        var created = RoofStructuralAttachedManualDataRules.Create(
            "FF01", identity, key, creationKind, 140d,
            RoofStructuralHeightMode.Explicit, 220d);
        Assert.True(created.IsValid);
        Assert.Equal(140d, created.Data!.WidthMm);
        Assert.Equal(RoofStructuralHeightMode.Explicit, created.Data.HeightMode);
        Assert.Equal(220d, created.Data.ExplicitHeightMm);
        Assert.Equal(role, created.Data.SourceRole);
        Assert.Equal(creationKind, created.Data.CreationKind);
        Assert.Equal(RoofStructuralAttachedManualDataSchema.CurrentVersion, created.Data.SchemaVersion);
    }

    [Fact]
    public void RigidCopy_MatchesLiveSourcePlanForExactHostThousandMmCopy()
    {
        // HOST M1: native COPY clones the live Line. Matching must accept that
        // exact Plan XY vector (1000 mm diagonal copy from the HOST retest).
        var liveSource = new RoofSegment3D(new(35351.85656854813, 10951.930768028607, 0),
            new(38351.85656854813, 13951.930768028607, 0));
        var clone = new RoofSegment3D(
            new(liveSource.Start.X + 1000d, liveSource.Start.Y + 1000d, 0),
            new(liveSource.End.X + 1000d, liveSource.End.Y + 1000d, 0));
        Assert.True(RoofStructuralManualPlacementRules.TryMatchRigidPlanCopy(
            liveSource, clone, out var dx, out var dy));
        Assert.Equal(1000d, dx, 6);
        Assert.Equal(1000d, dy, 6);
        Assert.False(RoofStructuralManualPlacementRules.TryMatchRigidPlanCopy(
            liveSource,
            new RoofSegment3D(
                new(clone.Start.X, clone.Start.Y, 0),
                new(clone.End.X + 5d, clone.End.Y, 0)),
            out _, out _));
    }

    [Fact]
    public void CopyLifecycle_ManualPhysicalKeyIncreasesOwnerCardinalityIndependentlyOfGenerated()
    {
        var generated = new[]
        {
            new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 1, 4),
            new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 2, 5),
            new RoofStructuralLogicalKey(RoofStructuralRole.Valley, 3, 6),
            new RoofStructuralLogicalKey(RoofStructuralRole.Valley, 4, 7),
        };
        var manualId = RoofStructuralAttachedManualIdentityRules.Create();
        var keys = generated.Select(key => key.ToString())
            .Append(RoofStructuralAttachedManualIdentityRules.PhysicalKey(manualId))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(5, keys.Count);
        Assert.Contains(RoofStructuralAttachedManualIdentityRules.PhysicalKey(manualId), keys);
        Assert.DoesNotContain("Hip|1|4", keys.Where(key => key.StartsWith("ManualStructural:", StringComparison.Ordinal)));
        Assert.True(RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(manualId)));
    }
}
