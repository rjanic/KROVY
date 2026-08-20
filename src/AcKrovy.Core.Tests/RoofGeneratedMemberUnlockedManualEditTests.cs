using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberUnlockedManualEditTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofGeneratedMemberKey Face0Station0 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 0);
    private static readonly RoofGeneratedMemberKey Face0Station3 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 3);
    private static readonly RoofGeneratedMemberKey Face0Station6 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 6);

    [Fact]
    public void UnlockedSimpleMove_PersistsCanonicalLocalTranslation()
    {
        var canonical = Horizontal(5000d);
        var observed = Translate(canonical, 0d, 400d);
        AssertMove(canonical, canonical, observed, along: 0d, lateral: 400d, existing: null);
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(5000d),
            Signature(5000d)));
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, observed));
    }

    [Fact]
    public void AlongOnly_AndLateralOnly_AndCombinedLocalMove()
    {
        var canonical = Horizontal(5000d);
        AssertMove(canonical, canonical, Translate(canonical, 120d, 0d), 120d, 0d, null);
        AssertMove(canonical, canonical, Translate(canonical, 0d, -80d), 0d, -80d, null);
        AssertMove(canonical, canonical, Translate(canonical, 90d, 40d), 90d, 40d, null);
    }

    [Fact]
    public void RepeatedMove_Composes_AndReturnToCanonicalNormalizesAway()
    {
        var canonical = Horizontal(5000d);
        var first = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            null, Face0Station0, "K4", 0d, 150d);
        Assert.Equal(150d, first!.LateralMm, 6);
        var second = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            first, Face0Station0, "K4", 0d, -40d);
        Assert.Equal(110d, second!.LateralMm, 6);
        Assert.Equal(0d, second.RotationRadians, 6);
        Assert.Null(RoofGeneratedMemberOverrideMath.ComposeTranslation(
            second, Face0Station0, "K4", 0d, -110d));
    }

    [Fact]
    public void MoveAfterTrim_PreservesEndOffset_AndLength()
    {
        var canonical = Horizontal(5000d);
        var trim = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station0, "K4", 0d, -300d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, trim, out var baseline));
        Assert.Equal(4700d, baseline.LengthMm, 6);
        var observed = Translate(baseline, 0d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            baseline, observed, ZUp, out var world, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.True(RoofGeneratedMemberOverrideMath.TryDecomposeInPlane(
            canonical, ZUp, world, out var along, out var lateral));
        var composed = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            trim, Face0Station0, "K4", along, lateral);
        Assert.Equal(150d, composed!.LateralMm, 6);
        Assert.Equal(-300d, composed.EndOffsetMm, 6);
        Assert.Equal(0d, composed.RotationRadians, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.Equal(4700d, replayed.LengthMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(4700d),
            Signature(4700d)));
    }

    [Fact]
    public void TrimAfterMove_ComposesEndpointOntoExistingTranslation()
    {
        var canonical = Horizontal(5000d);
        var move = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            null, Face0Station0, "K4", 0d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, move, out var baseline));
        var trimmed = new RoofGeneratedMemberGeometry(
            baseline.Start,
            new(baseline.End.X - 200d, baseline.End.Y, baseline.End.Z));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, trimmed, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            move, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(150d, composed!.LateralMm, 6);
        Assert.Equal(-200d, composed.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(trimmed, replayed));
    }

    [Fact]
    public void MoveAfterExtend_KeepsPositiveEndOffset()
    {
        var canonical = Horizontal(5000d);
        var extend = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station0, "K4", 0d, 250d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, extend, out var baseline));
        var observed = Translate(baseline, 80d, 0d);
        AssertMove(canonical, baseline, observed, 80d, 0d, extend);
        Assert.Equal(250d, RoofGeneratedMemberOverrideMath.ComposeTranslation(
            extend, Face0Station0, "K4", 80d, 0d)!.EndOffsetMm, 6);
    }

    [Fact]
    public void OffPlaneMove_IsRejected()
    {
        var canonical = Horizontal(4000d);
        var lifted = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 40d),
            new RoofPoint3D(4000d, 0d, 40d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            canonical, lifted, ZUp, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.OffPlane, reason);
    }

    [Fact]
    public void LengthChangingMove_IsRejectedAsNotPureTranslationOrLength()
    {
        var canonical = Horizontal(4000d);
        var longer = new RoofGeneratedMemberGeometry(canonical.Start, new(4300d, 150d, 0d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            canonical, longer, ZUp, out _, out _, out var reason));
        Assert.True(
            reason == RoofGeneratedMemberManualEditReason.LengthChanged ||
            reason == RoofGeneratedMemberManualEditReason.NotPureTranslation);
    }

    [Fact]
    public void InPlaneRotate_AroundNonOriginBasePoint_PersistsRigidTransform()
    {
        var canonical = Horizontal(5000d);
        var observed = RotateAround(canonical, new RoofPoint3D(800d, 200d, 0d), 0.14d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyRigidEqualLength(
            canonical, observed, ZUp, out var accepted, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(canonical.LengthMm, accepted.LengthMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, accepted, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.NotNull(overrideData);
        Assert.NotEqual(0d, overrideData!.RotationRadians);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(accepted, replayed));
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(5000d),
            Signature(5000d)));
    }

    [Fact]
    public void PositiveAndNegativeRotations_Compose()
    {
        var canonical = Horizontal(4200d);
        var plus = RotateAround(canonical, canonical.Start, 5d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, plus, ZUp, Face0Station0, "K4", out var first));
        var minus = RotateAround(plus, plus.Start, -2d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, minus, ZUp, Face0Station0, "K4", out var composed));
        Assert.Equal(3d * Math.PI / 180d, composed!.RotationRadians, 8);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(minus, replayed));
    }

    [Fact]
    public void MoveThenRotate_AndRotateThenMove_ReplayFinalGeometry()
    {
        var canonical = Horizontal(4800d);
        var moved = Translate(canonical, 0d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, ZUp, Face0Station0, "K4", out var afterMove));
        var rotated = RotateAround(moved, moved.Start, 0.12d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, rotated, ZUp, Face0Station0, "K4", out var moveThenRotate));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, moveThenRotate, out var replayedA));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotated, replayedA));

        var firstRotate = RotateAround(canonical, canonical.Start, 0.12d);
        var movedAfter = Translate(firstRotate, 0d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, movedAfter, ZUp, Face0Station0, "K4", out var rotateThenMove));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, rotateThenMove, out var replayedB));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(movedAfter, replayedB));
    }

    [Fact]
    public void TrimThenRotate_AndRotateThenExtend_PreserveEndpointSemantics()
    {
        var canonical = Horizontal(5000d);
        var trimmed = new RoofGeneratedMemberGeometry(canonical.Start, new(4700d, 0d, 0d));
        var rotatedTrim = RotateAround(trimmed, trimmed.Start, 0.2d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, rotatedTrim, ZUp, Face0Station0, "K4", out var trimThenRotate));
        Assert.NotEqual(0d, trimThenRotate!.EndOffsetMm);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, trimThenRotate, out var replayedTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotatedTrim, replayedTrim));

        var rotated = RotateAround(canonical, canonical.Start, 0.18d);
        var along = Unit(Subtract(rotated.End, rotated.Start));
        var extended = new RoofGeneratedMemberGeometry(
            rotated.Start,
            Add(rotated.End, Scale(along, 180d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, extended, ZUp, Face0Station0, "K4", out var rotateThenExtend));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, rotateThenExtend, out var replayedExtend));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(extended, replayedExtend));
    }

    [Fact]
    public void OffPlaneRotate_IsRejected()
    {
        var canonical = Horizontal(3000d);
        var tipped = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new RoofPoint3D(3000d, 0d, 80d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyRigidEqualLength(
            canonical, tipped, ZUp, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.OffPlane, reason);
    }

    [Fact]
    public void GripAlongLogicalEnd_IsSameAsTrimOrExtend()
    {
        var canonical = Horizontal(5000d);
        var shortened = new RoofGeneratedMemberGeometry(canonical.Start, new(4700d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, shortened, ZUp, out _, out var endDelta, out _, out var shortenReason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, shortenReason);
        Assert.Equal(-300d, endDelta, 6);

        var extended = new RoofGeneratedMemberGeometry(canonical.Start, new(5300d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, extended, ZUp, out _, out var extendDelta, out _, out var extendReason));
        Assert.Equal(300d, extendDelta, 6);
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(5000d),
            Signature(4700d)));
    }

    [Fact]
    public void ReversedLineGrip_MapsToLogicalEnd()
    {
        var canonical = Horizontal(5000d);
        var reversed = new RoofGeneratedMemberGeometry(new(4700d, 0d, 0d), canonical.Start);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, reversed, ZUp, out var startDelta, out var endDelta, out _, out _));
        Assert.Equal(0d, startDelta, 6);
        Assert.Equal(-300d, endDelta, 6);
    }

    [Fact]
    public void PriorTrimThenGrip_ComposesOffsets()
    {
        var canonical = Horizontal(5000d);
        var first = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station0, "K4", 0d, -200d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, first, out var baseline));
        var secondObserved = new RoofGeneratedMemberGeometry(baseline.Start, new(baseline.End.X - 150d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, secondObserved, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            first, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(-350d, composed!.EndOffsetMm, 6);
    }

    [Fact]
    public void PriorMoveThenGrip_PreservesTranslation()
    {
        var canonical = Horizontal(5000d);
        var move = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            null, Face0Station0, "K4", 40d, 90d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, move, out var baseline));
        var gripped = new RoofGeneratedMemberGeometry(baseline.Start, new(baseline.End.X - 120d, baseline.End.Y, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, gripped, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            move, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(40d, composed!.AlongMm, 6);
        Assert.Equal(90d, composed.LateralMm, 6);
        Assert.Equal(-120d, composed.EndOffsetMm, 6);
    }

    [Fact]
    public void InPlaneAngledEndpoint_IsRepresentableByExistingOverrideModel()
    {
        var canonical = Horizontal(5000d);
        var angled = new RoofGeneratedMemberGeometry(canonical.Start, new(4800d, 250d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        Assert.True(RoofGeneratedMemberOverrideMath.TryProjectObserved(
            angled, basis, out var projected, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, projected, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.NotNull(overrideData);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(projected, replayed));
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(canonical.LengthMm),
            Signature(projected.LengthMm)));
    }

    [Fact]
    public void OffPlaneAngledGrip_IsRejected()
    {
        var canonical = Horizontal(5000d);
        var offPlane = new RoofGeneratedMemberGeometry(canonical.Start, new(4800d, 0d, 40d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        Assert.False(RoofGeneratedMemberOverrideMath.TryProjectObserved(
            offPlane, basis, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.OffPlane, reason);
    }

    [Fact]
    public void Suppression_PersistsKeyAndReservedElementId_AndSkipsMaterialization()
    {
        var suppressed = RoofGeneratedMemberOverride.Suppress(Face0Station3, "K7");
        Assert.True(suppressed.Suppressed);
        Assert.Equal(Face0Station3, suppressed.Key);
        Assert.Equal("K7", suppressed.ReservedElementId);
        var set = new RoofManualOverrideSet().Upsert(suppressed);
        var rafter = new SimpleGableRafter(
            RafterRoofFace.Face0,
            3,
            4,
            0.5d,
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(5000d, 0d),
            35d);
        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            rafter, 0d, ZUp, set, out var geometry, out var isSuppressed));
        Assert.True(isSuppressed);
        Assert.Null(geometry);
    }

    [Fact]
    public void DormantSuppression_ReactivatesWhenLogicalKeyReturns()
    {
        var set = new RoofManualOverrideSet().Upsert(
            RoofGeneratedMemberOverride.Suppress(Face0Station6, "K9"));
        Assert.Null(set.FindMapped(Face0Station6, stationCount: 4));
        Assert.Single(set.FindDormant(4));
        var restored = set.FindMapped(Face0Station6, stationCount: 8);
        Assert.NotNull(restored);
        Assert.True(restored!.Suppressed);
        Assert.Equal("K9", restored.ReservedElementId);
    }

    [Fact]
    public void ResizeReplay_KeepsRelativeMoveRotateTrimAndSuppression()
    {
        var original = Horizontal(5000d);
        var edited = Translate(
            RotateAround(
                new RoofGeneratedMemberGeometry(original.Start, new(4700d, 0d, 0d)),
                original.Start,
                0.1d),
            0d,
            120d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            original, edited, ZUp, Face0Station0, "K4", out var overrideData));
        var resized = Horizontal(5600d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(resized, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(original, ZUp, overrideData, out var originalReplay));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(edited, originalReplay));
        Assert.InRange(Math.Abs(replayed.LengthMm - originalReplay.LengthMm), 0d, 700d);

        var suppressedSet = new RoofManualOverrideSet().Upsert(
            RoofGeneratedMemberOverride.Suppress(Face0Station0, "K4"));
        var rafter = new SimpleGableRafter(
            RafterRoofFace.Face0,
            0,
            5,
            0d,
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(5600d, 0d),
            35d);
        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            rafter, 0d, ZUp, suppressedSet, out _, out var suppressed));
        Assert.True(suppressed);
    }

    [Fact]
    public void WholeRoofRotateReplay_KeepsRelativeLateralAndRotation()
    {
        var original = Horizontal(4000d);
        var overrideData = new RoofGeneratedMemberOverride(
            Face0Station0,
            false,
            0d,
            150d,
            5d * Math.PI / 180d,
            0d,
            0d,
            "K4");
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(original, ZUp, overrideData, out _));
        var rotatedCanonical = RotateAround(original, new RoofPoint3D(0d, 0d, 0d), Math.PI / 6d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(
            rotatedCanonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(rotatedCanonical, ZUp, out var basis));
        Assert.Equal(150d, Dot(Subtract(replayed.Start, rotatedCanonical.Start), basis.AxisV), 5);
        Assert.Equal(5d * Math.PI / 180d, overrideData.RotationRadians, 8);
    }

    [Fact]
    public void CombinationPipeline_TrimMoveRotateGrip_ComposesDeterministically()
    {
        var canonical = Horizontal(5000d);
        var trimmed = new RoofGeneratedMemberGeometry(canonical.Start, new(4700d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, trimmed, ZUp, Face0Station0, "K4", out var afterTrim));
        var moved = Translate(trimmed, 0d, 110d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, ZUp, Face0Station0, "K4", out var afterMove));
        var rotated = RotateAround(moved, moved.Start, 0.08d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, rotated, ZUp, Face0Station0, "K4", out var afterRotate));
        var along = Unit(Subtract(rotated.End, rotated.Start));
        var gripped = new RoofGeneratedMemberGeometry(
            rotated.Start,
            Add(rotated.End, Scale(along, -90d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, gripped, ZUp, Face0Station0, "K4", out var afterGrip));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterGrip, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(gripped, replayed));
        Assert.False(afterGrip!.Suppressed);
        Assert.Equal("K4", afterGrip.ReservedElementId);
    }

    [Fact]
    public void SuppressingUniqueK7_DoesNotCompactRemainingNumbers()
    {
        var remainingA = Measurement("K8", 2800d);
        var remainingB = Measurement("K9", 2600d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(remainingA, false),
            new TimberElementItemNumberingCandidate(remainingB, false),
        ]);
        Assert.Equal("K8", result[0].ElementId);
        Assert.Equal("K9", result[1].ElementId);
        Assert.DoesNotContain("K7", result.Select(item => item.ElementId));
    }

    [Fact]
    public void MoveWithUniformForeignZ_IsAcceptedAfterNormalization()
    {
        var canonical = Horizontal(5000d);
        var rawObserved = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 400d, 1335.853d),
            new RoofPoint3D(5000d, 400d, 1335.853d));

        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var observed = RoofGeneratedMemberOverrideMath.NormalizeToBasis(rawObserved, basis, out var maxZDelta);
        Assert.Equal(1335.853d, maxZDelta, 6);

        AssertMove(canonical, canonical, observed, along: 0d, lateral: 400d, existing: null);
    }

    [Fact]
    public void GripSnapToRidgeWithLargeZ_IsAcceptedAfterNormalization()
    {
        var canonical = Horizontal(5000d);
        var rawObserved = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new RoofPoint3D(5300d, 0d, 1335.853d));

        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var observed = RoofGeneratedMemberOverrideMath.NormalizeToBasis(rawObserved, basis, out var maxZDelta);
        Assert.Equal(1335.853d, maxZDelta, 6);

        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, observed, ZUp, out _, out var endDelta, out var accepted, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(300d, endDelta, 6);
        Assert.Equal(0d, accepted.End.Z, 6);
    }

    [Fact]
    public void TargetedRecalcCommands_IncludeMoveRotateGripStretchAndBreak()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("MOVE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("ROTATE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("GRIP_STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("BREAK"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("ERASE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsClassicStretch("STRETCH"));
    }

    private static void AssertMove(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry baseline,
        RoofGeneratedMemberGeometry observed,
        double along,
        double lateral,
        RoofGeneratedMemberOverride? existing)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            baseline, observed, ZUp, out var world, out var accepted, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(baseline.LengthMm, accepted.LengthMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryDecomposeInPlane(
            canonical, ZUp, world, out var alongDelta, out var lateralDelta));
        var composed = RoofGeneratedMemberOverrideMath.ComposeTranslation(
            existing, Face0Station0, existing?.ReservedElementId ?? "K4", alongDelta, lateralDelta);
        Assert.NotNull(composed);
        Assert.Equal(along, composed!.AlongMm, 6);
        Assert.Equal(lateral, composed.LateralMm, 6);
        Assert.Equal(existing?.RotationRadians ?? 0d, composed.RotationRadians, 8);
        Assert.Equal(existing?.StartOffsetMm ?? 0d, composed.StartOffsetMm, 6);
        Assert.Equal(existing?.EndOffsetMm ?? 0d, composed.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
    }

    private static TimberElementSignature Signature(double planLengthMm) =>
        RoofGeneratedMemberRecalcScopeRules.SignatureFrom(Rafter("K4"), planLengthMm);

    private static TimberElementData Rafter(string elementId) =>
        TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = elementId,
            SlopeDegrees = 35d,
        };

    private static TimberElementMeasurement Measurement(string elementId, double planLengthMm) =>
        TimberCalculator.Measure(Rafter(elementId), planLengthMm);

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));

    private static RoofGeneratedMemberGeometry Translate(
        RoofGeneratedMemberGeometry geometry,
        double along,
        double lateral) =>
        new(
            new RoofPoint3D(geometry.Start.X + along, geometry.Start.Y + lateral, geometry.Start.Z),
            new RoofPoint3D(geometry.End.X + along, geometry.End.Y + lateral, geometry.End.Z));

    private static RoofGeneratedMemberGeometry RotateAround(
        RoofGeneratedMemberGeometry geometry,
        RoofPoint3D origin,
        double radians) =>
        new(
            RotatePoint(geometry.Start, origin, radians),
            RotatePoint(geometry.End, origin, radians));

    private static RoofPoint3D RotatePoint(RoofPoint3D point, RoofPoint3D origin, double radians)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new RoofPoint3D(
            origin.X + dx * cos - dy * sin,
            origin.Y + dx * sin + dy * cos,
            point.Z);
    }

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static RoofPoint3D Unit(RoofPoint3D vector)
    {
        var length = Math.Sqrt(Dot(vector, vector));
        return Scale(vector, 1d / length);
    }
}
