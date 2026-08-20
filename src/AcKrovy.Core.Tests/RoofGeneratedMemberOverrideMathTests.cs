using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberOverrideMathTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofPoint3D TiltedNormal = Normalize(new(0.2d, 0d, 1d));
    private static readonly RoofGeneratedMemberKey Face0Station0 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 0);
    private static readonly RoofGeneratedMemberKey Face1Station0 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 0);

    [Fact]
    public void DefaultEditState_IsLocked()
    {
        var data = new RoofDefinitionData(
            RoofDefinitionDataSchema.CurrentVersion,
            RoofKind.SimpleGable,
            35d,
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.CounterClockwise,
                10000d,
                6000d));
        Assert.Equal(RoofEditState.Locked, data.EditState);
        Assert.Empty(data.Overrides);
        Assert.Equal(RoofEditState.Locked, default(RoofEditState));
    }

    [Fact]
    public void Schema2Payload_MigratesToLockedWithNoOverrides()
    {
        const string payload = "2|SimpleGable|35|Edge01|4|CCW|10000|6000";
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(RoofDefinitionDataSchema.TopologyVersion, data!.SchemaVersion);
        Assert.Equal(RoofEditState.Locked, data.EditState);
        Assert.Empty(data.Overrides);
    }

    [Fact]
    public void Schema3_RoundTripsLockStateAndOverrides()
    {
        var original = Topology() with
        {
            SchemaVersion = RoofDefinitionDataSchema.CurrentVersion,
            EditState = RoofEditState.Unlocked,
            ManualOverrides =
            [
                new RoofGeneratedMemberOverride(Face0Station0, false, 150d, -40d, 0.1d, -300d, 100d, "K12"),
                RoofGeneratedMemberOverride.Suppress(Face1Station0, "K13"),
            ],
        };

        var payload = RoofDefinitionDataCodec.Encode(original);
        Assert.StartsWith("3|", payload, StringComparison.Ordinal);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out _));
        Assert.Equal(RoofEditState.Unlocked, decoded!.EditState);
        Assert.Equal(2, decoded.Overrides.Count);
        Assert.Equal(150d, decoded.Overrides[0].AlongMm);
        Assert.Equal(-300d, decoded.Overrides[0].StartOffsetMm);
        Assert.True(decoded.Overrides[1].Suppressed);
        Assert.Equal("K12", decoded.Overrides[0].ReservedElementId);
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(decoded));
    }

    [Fact]
    public void DuplicateOverrideKeys_AreRejected()
    {
        var data = Topology() with
        {
            SchemaVersion = RoofDefinitionDataSchema.CurrentVersion,
            ManualOverrides =
            [
                new RoofGeneratedMemberOverride(Face0Station0, false, 10d, 0d, 0d, 0d, 0d),
                new RoofGeneratedMemberOverride(Face0Station0, false, 20d, 0d, 0d, 0d, 0d),
            ],
        };
        Assert.False(RoofDefinitionDataCodec.TryValidate(data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidManualOverride, error);
    }

    [Fact]
    public void MoveRelativeOffset_AndComposition()
    {
        var canonical = Horizontal(5200d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            Translate(canonical, 150d, 40d),
            ZUp,
            Face0Station0,
            null,
            out var first));
        Assert.NotNull(first);
        Assert.Equal(150d, first!.AlongMm, 6);
        Assert.Equal(40d, first.LateralMm, 6);

        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, first, out var afterFirst));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            Translate(afterFirst, 50d, -10d),
            ZUp,
            Face0Station0,
            null,
            out var composed));
        Assert.NotNull(composed);
        Assert.Equal(200d, composed!.AlongMm, 6);
        Assert.Equal(30d, composed.LateralMm, 6);
    }

    [Fact]
    public void RotateRelativeAngle_WithTranslatedBasePoint()
    {
        var canonical = Horizontal(5200d);
        var rotated = RotateAround(canonical, new RoofPoint3D(800d, 0d, 0d), Math.PI / 2d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            rotated,
            ZUp,
            Face0Station0,
            null,
            out var overrideData));
        Assert.NotNull(overrideData);
        Assert.Equal(Math.PI / 2d, overrideData!.RotationRadians, 8);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotated, replayed));
    }

    [Fact]
    public void MultipleRotations_ComposeDeterministically()
    {
        var canonical = Horizontal(4000d);
        var once = RotateAround(canonical, canonical.Start, 0.2d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, once, ZUp, Face0Station0, null, out var first));
        var twice = RotateAround(once, once.Start, 0.15d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, twice, ZUp, Face0Station0, null, out var composed));
        Assert.Equal(0.35d, composed!.RotationRadians, 8);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(twice, replayed));
    }

    [Fact]
    public void StartTrim_EndTrim_AndExtension()
    {
        var canonical = Horizontal(5200d);
        AssertOffset(canonical, new RoofGeneratedMemberGeometry(new(300d, 0d, 0d), new(5200d, 0d, 0d)), start: -300d, end: 0d);
        AssertOffset(canonical, new RoofGeneratedMemberGeometry(new(0d, 0d, 0d), new(4900d, 0d, 0d)), start: 0d, end: -300d);
        AssertOffset(canonical, new RoofGeneratedMemberGeometry(new(0d, 0d, 0d), new(5400d, 0d, 0d)), start: 0d, end: 200d);
    }

    [Fact]
    public void TrimAfterMove_AndMoveAfterTrim()
    {
        var canonical = Horizontal(5200d);
        var moved = Translate(canonical, 150d, 0d);
        var trimmed = new RoofGeneratedMemberGeometry(moved.Start, new(moved.End.X - 250d, moved.End.Y, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, trimmed, ZUp, Face0Station0, null, out var afterTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterTrim, out var replayedTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(trimmed, replayedTrim));

        var startTrimmed = new RoofGeneratedMemberGeometry(new(300d, 0d, 0d), canonical.End);
        var movedAfterTrim = Translate(startTrimmed, 80d, 0d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, movedAfterTrim, ZUp, Face0Station0, null, out var afterMove));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, afterMove, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(movedAfterTrim, replayed));
    }

    [Fact]
    public void TrimAfterRotate_AndRotateAfterTrim()
    {
        var canonical = Horizontal(5000d);
        var rotated = RotateAround(canonical, canonical.Start, 0.3d);
        var along = Unit(Subtract(rotated.End, rotated.Start));
        var trimmed = new RoofGeneratedMemberGeometry(
            rotated.Start,
            Add(rotated.End, Scale(along, -200d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, trimmed, ZUp, Face0Station0, null, out var trimAfterRotate));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, trimAfterRotate, out var replayedTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(trimmed, replayedTrim));

        var firstTrim = new RoofGeneratedMemberGeometry(canonical.Start, new(4700d, 0d, 0d));
        var rotatedTrim = RotateAround(firstTrim, firstTrim.Start, 0.25d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, rotatedTrim, ZUp, Face0Station0, null, out var rotateAfterTrim));
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, rotateAfterTrim, out var replayedRotate));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(rotatedTrim, replayedRotate));
    }

    [Fact]
    public void Suppression_AndResetModel()
    {
        var set = new RoofManualOverrideSet()
            .Upsert(RoofGeneratedMemberOverride.Suppress(Face0Station0, "K4"))
            .Upsert(new RoofGeneratedMemberOverride(Face1Station0, false, 20d, 0d, 0d, 0d, 0d));
        Assert.Equal(1, set.SuppressedCount);
        Assert.Equal(1, set.GeometryOverrideCount);
        var resetOne = set.Remove(Face0Station0);
        Assert.Equal(0, resetOne.SuppressedCount);
        Assert.True(resetOne.TryGet(Face1Station0, out _));
        Assert.Equal(0, set.Clear().Count);
    }

    [Fact]
    public void CanonicalResize_ReplaysRelativeTrim()
    {
        var original = Horizontal(5200d);
        var trimmed = new RoofGeneratedMemberGeometry(original.Start, new(4900d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            original, trimmed, ZUp, Face0Station0, null, out var overrideData));
        var resized = Horizontal(5700d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(resized, ZUp, overrideData, out var replayed));
        Assert.Equal(5400d, replayed.LengthMm, 6);
        Assert.Equal(0d, replayed.Start.X, 6);
        Assert.Equal(5400d, replayed.End.X, 6);
    }

    [Fact]
    public void CanonicalRotateAndTranslate_ReplayOverride()
    {
        var original = Horizontal(4000d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            original,
            Translate(original, 120d, 30d),
            ZUp,
            Face0Station0,
            null,
            out var overrideData));
        var rotatedCanonical = RotateAround(original, new RoofPoint3D(0d, 0d, 0d), Math.PI / 6d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(
            rotatedCanonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(rotatedCanonical, ZUp, out var rotatedBasis));
        var expected = new RoofGeneratedMemberGeometry(
            Add(
                Add(rotatedCanonical.Start, Scale(rotatedBasis.AxisU, 120d)),
                Scale(rotatedBasis.AxisV, 30d)),
            Add(
                Add(rotatedCanonical.End, Scale(rotatedBasis.AxisU, 120d)),
                Scale(rotatedBasis.AxisV, 30d)));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(expected, replayed));
    }

    [Fact]
    public void NoOp_NormalizesAway()
    {
        var canonical = Horizontal(3000d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, canonical, ZUp, Face0Station0, null, out var overrideData));
        Assert.Null(overrideData);
        Assert.Null(RoofGeneratedMemberOverrideMath.Normalize(
            new RoofGeneratedMemberOverride(Face0Station0, false, 0.001d, 0d, 0d, 0d, 0d)));
    }

    [Fact]
    public void OffPlaneEdit_IsRejected()
    {
        var canonical = Horizontal(2000d);
        var lifted = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 50d),
            new RoofPoint3D(2000d, 0d, 50d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, lifted, ZUp, Face0Station0, null, out _));
    }

    [Fact]
    public void LogicalKey_EqualityAndPlaneSeparation()
    {
        var a = RoofGeneratedMemberKey.From(new RoofGeneratedTimberData(
            1, "A", RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 2, 5, 800d, "sig"));
        var b = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 2);
        var otherFace = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 2);
        Assert.Equal(a, b);
        Assert.NotEqual(a, otherFace);
        Assert.True(a.MapsToCurrentLayout(5));
        Assert.False(new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 6).MapsToCurrentLayout(5));
    }

    [Fact]
    public void DormantOverride_IsNotMappedWhenStationCountShrinks()
    {
        var high = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 6);
        var set = new RoofManualOverrideSet().Upsert(
            new RoofGeneratedMemberOverride(high, false, 10d, 0d, 0d, 0d, 0d));
        Assert.Null(set.FindMapped(high, stationCount: 5));
        Assert.Single(set.FindDormant(5));
        Assert.NotNull(set.FindMapped(high, stationCount: 8));
    }

    [Fact]
    public void TiltedPlane_DoesNotBreakGenericMath()
    {
        var canonical = new RoofGeneratedMemberGeometry(new(0d, 0d, 0d), new(4000d, 0d, 800d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, TiltedNormal, out _));
        var along = Unit(Subtract(canonical.End, canonical.Start));
        var moved = new RoofGeneratedMemberGeometry(
            Add(canonical.Start, Scale(along, 100d)),
            Add(canonical.End, Scale(along, 100d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, TiltedNormal, Face0Station0, null, out var overrideData));
        Assert.Equal(100d, overrideData!.AlongMm, 5);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, TiltedNormal, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(moved, replayed));
    }

    [Fact]
    public void NormalizeToBasis_PreservesPlanarEdit_AndDiscardsForeignZ()
    {
        var canonical = Horizontal(5000d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var rawObserved = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 0d),
            new RoofPoint3D(5300d, 400d, 1335.853d));
        var normalized = RoofGeneratedMemberOverrideMath.NormalizeToBasis(rawObserved, basis, out var maxZDelta);
        Assert.Equal(1335.853d, maxZDelta, 6);
        Assert.Equal(0d, normalized.Start.Z, 6);
        Assert.Equal(0d, normalized.End.Z, 6);
        Assert.Equal(5300d, normalized.End.X, 6);
        Assert.Equal(400d, normalized.End.Y, 6);
    }

    [Fact]
    public void UnknownInvalidKey_IsPreservedAsDormant()
    {
        var payload = RoofGeneratedMemberOverrideCodec.Encode(
        [
            new RoofGeneratedMemberOverride(
                new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 99),
                false,
                12d,
                0d,
                0d,
                0d,
                0d),
        ]);
        Assert.True(RoofGeneratedMemberOverrideCodec.TryDecode(payload, out var decoded, out _));
        Assert.Equal(99, decoded[0].Key.StationIndex);
        Assert.False(decoded[0].Key.MapsToCurrentLayout(4));
    }

    private static void AssertOffset(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed,
        double start,
        double end)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, null, out var overrideData));
        Assert.NotNull(overrideData);
        Assert.Equal(start, overrideData!.StartOffsetMm, 6);
        Assert.Equal(end, overrideData.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
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
        double radians)
    {
        return new RoofGeneratedMemberGeometry(
            RotatePoint(geometry.Start, origin, radians),
            RotatePoint(geometry.End, origin, radians));
    }

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

    private static RoofPoint3D Unit(RoofPoint3D vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
        return Scale(vector, 1d / length);
    }

    private static RoofPoint3D Normalize(RoofPoint3D vector) => Unit(vector);
}
