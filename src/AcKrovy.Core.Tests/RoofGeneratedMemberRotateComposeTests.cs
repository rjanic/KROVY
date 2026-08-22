using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberRotateComposeTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofGeneratedMemberKey Face0Station4 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 4);

    private static readonly RoofGeneratedMemberGeometry HostBefore = new(
        new RoofPoint3D(33721.294d, 14876.525d, 0d),
        new RoofPoint3D(33721.294d, 17741.272d, 0d));

    private static readonly RoofGeneratedMemberGeometry HostAfter = new(
        new RoofPoint3D(32430.266d, 15688.462d, 0d),
        new RoofPoint3D(35012.321d, 16929.335d, 0d));

    [Fact]
    public void HostUnlockedRotate_ArbitraryBasePoint_ComposesWithoutFailure()
    {
        Assert.Equal(2864.747d, HostBefore.LengthMm, 3);
        Assert.Equal(2864.747d, HostAfter.LengthMm, 3);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(HostBefore, ZUp, out var basis));
        var normalized = RoofGeneratedMemberOverrideMath.NormalizeToBasis(HostAfter, basis, out _);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyRigidEqualLength(
            HostBefore, normalized, ZUp, out var accepted, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            HostBefore, accepted, ZUp, Face0Station4, null, "K6", out var overrideData, out var failure));
        Assert.Null(failure);
        Assert.NotNull(overrideData);
        Assert.Equal(0d, overrideData!.StartOffsetMm, 6);
        Assert.Equal(0d, overrideData.EndOffsetMm, 6);
        Assert.NotEqual(0d, overrideData.RotationRadians);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(HostBefore, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(accepted, replayed));
        Assert.True(
            RoofGeneratedMemberOverrideMath.MaxEndpointErrorMm(accepted, replayed) <=
            RoofGeneratedMemberOverrideMath.LengthToleranceMm);
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            Signature(HostBefore.LengthMm),
            Signature(HostAfter.LengthMm)));
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(HostBefore, HostAfter));
        Assert.False(overrideData.Suppressed);
        Assert.Equal("K6", overrideData.ReservedElementId);
    }

    [Fact]
    public void HostUnlockedRotate_TryClassify_DoesNotReturnCompositionFailure()
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            HostBefore, HostAfter, ZUp, Face0Station4, "K6", out var overrideData));
        Assert.NotNull(overrideData);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(HostBefore, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(HostAfter, replayed));
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(-30d)]
    [InlineData(90d)]
    [InlineData(-90d)]
    [InlineData(179.4d)]
    [InlineData(37.5d)]
    public void PureRotate_RetainsLogicalEndpoints_ForCardinalAndArbitraryAngles(double degrees)
    {
        var canonical = HostBefore;
        var pivot = new RoofPoint3D(31000d, 14000d, 0d);
        var observed = RotateAround(canonical, pivot, degrees * Math.PI / 180d);
        AssertCompose(canonical, observed, existing: null, expectedStart: 0d, expectedEnd: 0d);
        AssertSameLogicalOrientation(canonical, observed);
    }

    [Fact]
    public void SameLengthTranslatedAndRotatedLine_IsRepresentable()
    {
        var canonical = Horizontal(4000d);
        var moved = Translate(canonical, 120d, -80d);
        var observed = RotateAround(moved, new RoofPoint3D(2500d, 900d, 0d), 0.41d);
        AssertCompose(canonical, observed, null, 0d, 0d);
    }

    [Fact]
    public void SequentialRotatePlus5ThenMinus2_EqualsNetPlus3RelativeOrientation()
    {
        var canonical = HostBefore;
        var plus5 = RotateAround(canonical, new RoofPoint3D(30000d, 12000d, 0d), 5d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, plus5, ZUp, Face0Station4, null, "K6", out var first, out _));
        var minus2 = RotateAround(plus5, new RoofPoint3D(36000d, 19000d, 0d), -2d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, minus2, ZUp, Face0Station4, first, "K6", out var composed, out _));
        Assert.NotNull(composed);
        Assert.Equal(3d * Math.PI / 180d, composed!.RotationRadians, 8);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(minus2, replayed));
    }

    [Fact]
    public void MoveThenRotate_AndRotateThenMove_ReplayEachFinalGeometry()
    {
        var canonical = Horizontal(4800d);
        var moved = Translate(canonical, 0d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, moved, ZUp, Face0Station4, null, "K4", out var afterMove, out _));
        var rotated = RotateAround(moved, new RoofPoint3D(900d, 400d, 0d), 0.22d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, rotated, ZUp, Face0Station4, afterMove, "K4", out var moveThenRotate, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, moveThenRotate, out var replayedA));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotated, replayedA));
        Assert.Equal(0d, moveThenRotate!.StartOffsetMm, 6);
        Assert.Equal(0d, moveThenRotate.EndOffsetMm, 6);

        var firstRotate = RotateAround(canonical, new RoofPoint3D(-200d, 50d, 0d), 0.22d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, firstRotate, ZUp, Face0Station4, null, "K4", out var afterRotate, out _));
        var movedAfter = Translate(firstRotate, 40d, 150d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, movedAfter, ZUp, Face0Station4, afterRotate, "K4", out var rotateThenMove, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, rotateThenMove, out var replayedB));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(movedAfter, replayedB));
    }

    [Fact]
    public void TrimThenRotate_PreservesExistingEndOffset()
    {
        var canonical = Horizontal(5000d);
        var trim = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station4, "K4", 0d, -300d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, trim, out var baseline));
        var rotated = RotateAround(baseline, new RoofPoint3D(800d, 250d, 0d), 0.31d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, rotated, ZUp, Face0Station4, trim, "K4", out var composed, out var failure));
        Assert.Null(failure);
        Assert.Equal(-300d, composed!.EndOffsetMm, 6);
        Assert.Equal(0d, composed.StartOffsetMm, 6);
        Assert.NotEqual(0d, composed.RotationRadians);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotated, replayed));
        Assert.Equal(baseline.LengthMm, rotated.LengthMm, 6);
    }

    [Fact]
    public void ExtendThenRotate_PreservesExistingEndOffset()
    {
        var canonical = Horizontal(5000d);
        var extend = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station4, "K4", 0d, 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, extend, out var baseline));
        var rotated = RotateAround(baseline, new RoofPoint3D(-400d, 120d, 0d), -0.27d);
        AssertCompose(canonical, rotated, extend, 0d, 180d);
    }

    [Fact]
    public void RotateThenTrim_AndRotateThenExtend_KeepRotationAndComposeOffsets()
    {
        var canonical = Horizontal(5000d);
        var rotated = RotateAround(canonical, new RoofPoint3D(200d, 900d, 0d), 0.18d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, rotated, ZUp, Face0Station4, null, "K4", out var afterRotate, out _));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterRotate, out var baseline));

        var along = Unit(Subtract(baseline.End, baseline.Start));
        var trimmed = new RoofGeneratedMemberGeometry(
            baseline.Start,
            Subtract(baseline.End, Scale(along, 220d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, trimmed, ZUp, out var startDelta, out var endDelta, out _, out var trimReason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, trimReason);
        var afterTrim = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            afterRotate, Face0Station4, "K4", startDelta, endDelta);
        Assert.Equal(0d, afterTrim!.StartOffsetMm, 6);
        Assert.Equal(-220d, afterTrim.EndOffsetMm, 6);
        Assert.Equal(afterRotate!.RotationRadians, afterTrim.RotationRadians, 8);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterTrim, out var replayedTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(trimmed, replayedTrim));

        var extended = new RoofGeneratedMemberGeometry(
            baseline.Start,
            Add(baseline.End, Scale(along, 140d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, extended, ZUp, out _, out var extendDelta, out _, out var extendReason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, extendReason);
        var afterExtend = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            afterRotate, Face0Station4, "K4", 0d, extendDelta);
        Assert.Equal(140d, afterExtend!.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterExtend, out var replayedExtend));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(extended, replayedExtend));
    }

    [Fact]
    public void ExistingStartAndBothEndpointOffsets_SurviveRotate()
    {
        var canonical = Horizontal(5200d);
        var startOnly = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station4, "K4", -250d, 0d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, startOnly, out var startBaseline));
        var rotatedStart = RotateAround(startBaseline, new RoofPoint3D(100d, -300d, 0d), 0.44d);
        AssertCompose(canonical, rotatedStart, startOnly, -250d, 0d);

        var both = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station4, "K4", -120d, -180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, both, out var bothBaseline));
        var rotatedBoth = RotateAround(bothBaseline, new RoofPoint3D(700d, 50d, 0d), -0.55d);
        AssertCompose(canonical, rotatedBoth, both, -120d, -180d);
    }

    [Fact]
    public void SaveReopenCodec_ReplaysHostRotateGeometry()
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            HostBefore, HostAfter, ZUp, Face0Station4, null, "K6", out var original, out _));
        var data = Topology() with
        {
            SchemaVersion = RoofDefinitionDataSchema.CurrentVersion,
            EditState = RoofEditState.Unlocked,
            ManualOverrides = [original!],
        };
        var payload = RoofDefinitionDataCodec.Encode(data);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out _));
        Assert.Equal(RoofEditState.Unlocked, decoded!.EditState);
        Assert.Equal(5, decoded.SchemaVersion);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(
            HostBefore, ZUp, decoded.Overrides[0], out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(HostAfter, replayed));
        Assert.Equal("K6", decoded.Overrides[0].ReservedElementId);
    }

    [Fact]
    public void SupportedResize_ReplaysRelativeRotationOnNewCanonical()
    {
        var originalCanonical = Horizontal(4000d);
        var observed = RotateAround(originalCanonical, new RoofPoint3D(1500d, 400d, 0d), 0.2d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            originalCanonical, observed, ZUp, Face0Station4, null, "K6", out var overrideData, out _));
        var resizedCanonical = Horizontal(5500d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(
            resizedCanonical, ZUp, overrideData, out var resizedReplay));
        var resizedDir = Subtract(resizedReplay.End, resizedReplay.Start);
        var canonicalDir = Subtract(resizedCanonical.End, resizedCanonical.Start);
        var relative = SignedAngle(canonicalDir, resizedDir);
        Assert.Equal(0.2d, relative, 8);
        Assert.False(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, resizedReplay));
    }

    [Fact]
    public void WholeRoofRigidRotate_KeepsMemberOverrideRelative()
    {
        var canonical = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(1000d, 2000d, 0d),
            new RoofPoint3D(1000d, 6000d, 0d));
        var memberObserved = RotateAround(canonical, canonical.Start, 10d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical, memberObserved, ZUp, Face0Station4, null, "K6", out var overrideData, out _));
        Assert.Equal(10d * Math.PI / 180d, overrideData!.RotationRadians, 8);

        var roofPivot = new RoofPoint3D(0d, 0d, 0d);
        var roofCanonical = RotateAround(canonical, roofPivot, 30d * Math.PI / 180d);
        var expected = RotateAround(memberObserved, roofPivot, 30d * Math.PI / 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(roofCanonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(expected, replayed));
        Assert.Equal(10d * Math.PI / 180d, overrideData.RotationRadians, 8);
    }

    [Fact]
    public void LockedRotate_DoesNotPersistOverride_SourceContract()
    {
        var manual = RoofUxSourceContractText.Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("if (!supportedUnlocked)", manual);
        Assert.True(
            manual.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal) <
            manual.IndexOf("TryAcceptUnlockedEdits(", StringComparison.Ordinal));
        Assert.Contains("TryRecoverGeneratedMembersOnly", manual);
        Assert.DoesNotContain("TryComposeRigidKeepingEndpointOffsets",
            RoofUxSourceContractText.Member(
                manual,
                "private static OwnerEditOutcome ProcessOwner",
                "private static bool TryAcceptUnlockedEdits"));
    }

    [Fact]
    public void RotateWithForeignZ_IsProjectedBeforeCompose()
    {
        var raw = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(HostAfter.Start.X, HostAfter.Start.Y, 1335.853d),
            new RoofPoint3D(HostAfter.End.X, HostAfter.End.Y, 1335.853d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(HostBefore, ZUp, out var basis));
        var normalized = RoofGeneratedMemberOverrideMath.NormalizeToBasis(raw, basis, out var maxZ);
        Assert.Equal(1335.853d, maxZ, 6);
        Assert.Equal(0d, normalized.Start.Z, 6);
        Assert.Equal(0d, normalized.End.Z, 6);
        AssertCompose(HostBefore, normalized, null, 0d, 0d);
    }

    [Fact]
    public void NormalizeToBasis_DoesNotSwapLogicalEndpointsOnObtuseRotate()
    {
        var canonical = Horizontal(3000d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var rotated = RotateAround(canonical, canonical.Start, 2.4d);
        var normalized = RoofGeneratedMemberOverrideMath.NormalizeToBasis(rotated, basis, out _);
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotated, normalized));
        AssertSameLogicalOrientation(canonical, normalized);
    }

    [Fact]
    public void SignatureAndKNumber_UnchangedForPureRotate()
    {
        var before = Signature(HostBefore.LengthMm);
        var after = Signature(HostAfter.LengthMm);
        Assert.Equal(before, after);
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(before, after));
        Assert.Equal("K6", Rafter("K6").ElementId);
    }

    private static void AssertCompose(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed,
        RoofGeneratedMemberOverride? existing,
        double expectedStart,
        double expectedEnd)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical,
            observed,
            ZUp,
            Face0Station4,
            existing,
            existing?.ReservedElementId ?? "K6",
            out var overrideData,
            out var failure));
        Assert.Null(failure);
        Assert.NotNull(overrideData);
        Assert.Equal(expectedStart, overrideData!.StartOffsetMm, 6);
        Assert.Equal(expectedEnd, overrideData.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
        Assert.True(
            RoofGeneratedMemberOverrideMath.MaxEndpointErrorMm(observed, replayed) <=
            RoofGeneratedMemberOverrideMath.LengthToleranceMm);
    }

    private static void AssertSameLogicalOrientation(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed)
    {
        var canonicalDir = Subtract(canonical.End, canonical.Start);
        var observedDir = Subtract(observed.End, observed.Start);
        var angle = Math.Abs(SignedAngle(canonicalDir, observedDir));
        Assert.True(angle <= Math.PI + 1e-9d);
    }

    private static RoofDefinitionData Topology() =>
        new(
            RoofDefinitionDataSchema.TopologyVersion,
            RoofKind.SimpleGable,
            35d,
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.CounterClockwise,
                10000d,
                6000d));

    private static TimberElementSignature Signature(double planLengthMm) =>
        RoofGeneratedMemberRecalcScopeRules.SignatureFrom(Rafter("K6"), planLengthMm);

    private static TimberElementData Rafter(string elementId) =>
        TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = elementId,
            SlopeDegrees = 35d,
        };

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
        new(RotatePoint(geometry.Start, origin, radians), RotatePoint(geometry.End, origin, radians));

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

    private static double SignedAngle(RoofPoint3D from, RoofPoint3D to)
    {
        var fromLength = Math.Sqrt(Dot(from, from));
        var toLength = Math.Sqrt(Dot(to, to));
        var a = Scale(from, 1d / fromLength);
        var b = Scale(to, 1d / toLength);
        var sin = (a.X * b.Y) - (a.Y * b.X);
        var cos = Dot(a, b);
        return Math.Atan2(sin, cos);
    }

    private static RoofPoint3D Unit(RoofPoint3D vector) => Scale(vector, 1d / Math.Sqrt(Dot(vector, vector)));
}
