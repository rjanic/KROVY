using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualAnchorStabilityTests
{
    [Fact]
    public void Copy_WithPerpendicularCloserCandidate_RetainsSourceFrame()
    {
        var source = new RoofReanchorCandidate(new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 12), new(0, 0, 0), new(1850, 0, 0));
        var other = new RoofReanchorCandidate(new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 2), new(12000, 0, 0), new(12000, -1850, 0));
        var start = new RoofPoint3D(12000, 718, 0);
        var end = new RoofPoint3D(12000, -1132, 0);
        var selected = RoofAttachedManualReanchorRules.SelectRetainedAnchor(source.Key, [other, source], start, end);
        Assert.Equal(other.Key, RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(source.Key.MemberKind, [other, source], start, end)?.Key);
        Assert.Equal(source.Key, selected?.Key);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(selected!.Start, selected.End, start, end, out var relative));
        Assert.Equal(relative.U0Mm, relative.U1Mm);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(source.Start, source.End, relative, out var replayStart, out var replayEnd));
        Assert.Equal(start, replayStart);
        Assert.Equal(end, replayEnd);
        var movedStart = new RoofPoint3D(start.X + 85, start.Y + 150, 0);
        var movedEnd = new RoofPoint3D(end.X + 85, end.Y + 150, 0);
        Assert.Equal(source, RoofAttachedManualReanchorRules.SelectRetainedAnchor(source.Key, [other, source], movedStart, movedEnd));
        Assert.Equal(source, RoofAttachedManualReanchorRules.SelectRetainedAnchor(source.Key, [source, other], movedStart, movedEnd));
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Vertical, RidgeJoinMode.Overlap)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Horizontal, RidgeJoinMode.Overlap)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Meet)]
    [InlineData(LowerEndCutMode.Perpendicular, RidgeJoinMode.Overlap)]
    public void GeneratedCopy_ManualCopy_Move_TranslateCompleteBodyWithoutChangingZ(LowerEndCutMode lower, RidgeJoinMode ridge)
    {
        var footprint = RoofFootprintValidator.Validate(new([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint.Footprint!, new(45), RoofKind.Hip)).Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        var settings = new RoofAutomaticRafterPhysicalSettings(lower, ridge);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", roof.Topology, faces, layout, 3000, 80, 125, settings, out var generated));
        var source = generated!.Members.First(m => m.StartBoundaryRole == RoofRafterBoundaryRole.Eave && m.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        RoofSegment3D Shift(RoofSegment3D axis, double x, double y) => new(new(axis.Start.X + x, axis.Start.Y + y, 0), new(axis.End.X + x, axis.End.Y + y, 0));
        RoofAttachedManualTimberData Child(string handle, RoofSegment3D axis, string identity)
        {
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(source.PlanAxis.Start, source.PlanAxis.End, axis.Start, axis.End, out var relative));
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(source.PlanAxis.Start, source.PlanAxis.End,
                source.PlanAxis.Start, source.PlanAxis.End, out var reference));
            var data = new RoofAttachedManualTimberData(5, "AB", handle, RoofTimberChildRole.AttachedManual, source.MemberKey,
                relative, RoofAttachedManualOrigin.Copy, identity, reference);
            Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(RoofAttachedManualTimberDataCodec.Encode(data), out var persisted));
            return persisted!;
        }
        RoofAutomaticRafterPhysicalMember Build(RoofAttachedManualTimberData child, RoofSegment3D axis)
        {
            Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(roof.Topology, faces, generated, [new(child, axis, 80, 125)], 3000, settings, null, out var model, out var reason), reason);
            Assert.Same(source, model!.Members.Single(m => m.PhysicalIdentity == source.PhysicalIdentity));
            return model.Members.Single(m => m.AttachedManualIdentity is not null);
        }
        var axisA = Shift(source.PlanAxis, 9000, 7000);
        var a = Child("A", axisA, RoofAttachedManualIdentityRules.Create());
        var bodyA = Build(a, axisA);
        AssertTranslated(source, bodyA, 9000, 7000);
        var axisB = Shift(axisA, 1200, -700);
        var b = Child("B", axisB, RoofAttachedManualIdentityRules.Create());
        Assert.NotEqual(a.SemanticIdentity, b.SemanticIdentity);
        Assert.Equal(a.AnchorGeneratedMemberKey, b.AnchorGeneratedMemberKey);
        var bodyB = Build(b, axisB);
        AssertTranslated(bodyA, bodyB, 1200, -700);
        var movedAxis = Shift(axisB, -500, 1600);
        var moved = Child("B", movedAxis, b.SemanticIdentity!);
        Assert.Equal(b.SemanticIdentity, moved.SemanticIdentity);
        Assert.Equal(b.AnchorGeneratedMemberKey, moved.AnchorGeneratedMemberKey);
        Assert.NotEqual(b.RelativeSegment, moved.RelativeSegment);
        AssertTranslated(bodyB, Build(moved, movedAxis), -500, 1600);
        Assert.Equal(bodyA.SolidVertices, Build(a, axisA).SolidVertices);
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(roof.Topology, faces, generated,
            [new(a, axisA, 80, 125), new(moved, movedAxis, 80, 125)], 3000, settings, null, out var together, out _));
        Assert.Equal(generated.Members.Count + 2, together!.Members.Count);
        Assert.Equal(together.Members.Count, together.Members.Select(member => member.PhysicalIdentity).Distinct().Count());
        // No live/layout anchor: recover the SAME basis from world + persisted local
        // geometry. This is not a nearest-station re-anchor.
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(roof.Topology, new(0, [], string.Empty),
            new("AB", []), [new(moved, movedAxis, 80, 125)], 3000, settings, null, out var recovered, out var recoveryReason), recoveryReason);
        AssertTranslated(Build(moved, movedAxis), recovered!.Members.Single(), 0, 0);

        // HOST-style legacy source: child perpendicular to its own valid anchor.
        // A COPY must preserve A's actual body, not replace it with G's orientation.
        var dx = source.PlanAxis.End.X - source.PlanAxis.Start.X;
        var dy = source.PlanAxis.End.Y - source.PlanAxis.Start.Y;
        var perpendicular = new RoofSegment3D(axisA.Start, new(axisA.Start.X - dy, axisA.Start.Y + dx, 0));
        var legacy = Child("LEGACY", perpendicular, RoofAttachedManualIdentityRules.Create()) with
        { SchemaVersion = 4, PhysicalReferenceSegment = null };
        var legacyBody = Build(legacy, perpendicular);
        var legacyCloneAxis = Shift(perpendicular, 1400, -900);
        var legacyClone = Child("LEGACY_CLONE", legacyCloneAxis, RoofAttachedManualIdentityRules.Create()) with
        { PhysicalReferenceSegment = legacy.RelativeSegment };
        var legacyCloneBody = Build(legacyClone, legacyCloneAxis);
        AssertTranslated(legacyBody, legacyCloneBody, 1400, -900);
        var legacyMovedAxis = Shift(legacyCloneAxis, -150, 500);
        var legacyMoved = Child("LEGACY_CLONE", legacyMovedAxis, legacyClone.SemanticIdentity!) with
        { PhysicalReferenceSegment = legacyClone.PhysicalReferenceSegment };
        AssertTranslated(legacyCloneBody, Build(legacyMoved, legacyMovedAxis), -150, 500);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDegenerateAnchor_DoesNotAdoptPerpendicularCandidate(bool degenerate)
    {
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 23);
        var candidates = new List<RoofReanchorCandidate>
        {
            new(new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 7), new(0, 0, 0), new(0, 1850, 0)),
        };
        if (degenerate) candidates.Add(new(key, new(0, 0, 0), new(0, 0, 0)));
        Assert.Null(RoofAttachedManualReanchorRules.SelectRetainedAnchor(key, candidates, new(20, 0, 0), new(1870, 0, 0)));
    }

    private static void AssertTranslated(RoofAutomaticRafterPhysicalMember source, RoofAutomaticRafterPhysicalMember actual, double x, double y)
    {
        Assert.Equal(source.WidthMm, actual.WidthMm);
        Assert.Equal(source.HeightMm, actual.HeightMm);
        Assert.Equal(source.PhysicalLengthMm, actual.PhysicalLengthMm, 5);
        Assert.Equal(source.PitchDegrees, actual.PitchDegrees, 5);
        Check(source.SolidVertices, actual.SolidVertices);
        if (source.HorizontalCut is { } h) { Assert.NotNull(actual.HorizontalCut); Check(h.SourcePrismVertices, actual.HorizontalCut!.SourcePrismVertices); Check(h.CutFaceVertices, actual.HorizontalCut.CutFaceVertices); }
        if (source.RidgeOverlapCut is { } r) { Assert.NotNull(actual.RidgeOverlapCut); Check(r.SourcePrismVertices, actual.RidgeOverlapCut!.SourcePrismVertices); Check(r.CutFaceVertices, actual.RidgeOverlapCut.CutFaceVertices); }
        void Check(IReadOnlyList<RoofPoint3D> before, IReadOnlyList<RoofPoint3D> after)
        {
            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++) { Assert.Equal(before[i].X + x, after[i].X, 5); Assert.Equal(before[i].Y + y, after[i].Y, 5); Assert.Equal(before[i].Z, after[i].Z, 5); }
        }
    }
}
