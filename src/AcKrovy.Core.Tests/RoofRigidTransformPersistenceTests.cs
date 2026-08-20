using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRigidTransformPersistenceTests
{
    [Fact]
    public void SchemaModels_RejectMixedLegacyAndTopologyFields()
    {
        var source = Rectangle();
        var data = Create(source, 35d, RoofRidgeEdgeFamily.SourceEdge01) with
        {
            RidgeDirectionX = 1d,
        };

        Assert.False(RoofDefinitionDataCodec.TryValidate(data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor, error);

        var footprint = Validate(source);
        var legacy = LegacyData(
            footprint,
            Solve(source, footprint, 35d, RoofRidgeEdgeFamily.SourceEdge01)) with
        {
            RidgeEdgeFamily = RoofRidgeEdgeFamily.SourceEdge01,
        };
        Assert.False(RoofDefinitionDataCodec.TryValidate(legacy, out error));
        Assert.Equal(RoofDefinitionDataDecodeError.MalformedPayload, error);
    }

    [Fact]
    public void SchemaV2_RoundTripsTopologyRelativeFields()
    {
        var source = Rectangle();
        var data = Create(source, 37.1234567890123d, RoofRidgeEdgeFamily.SourceEdge01);

        var payload = RoofDefinitionDataCodec.Encode(data);
        var decoded = Decode(payload);

        Assert.Equal(3, decoded.SchemaVersion);
        Assert.Equal(RoofKind.SimpleGable, decoded.Kind);
        Assert.Equal(37.1234567890123d, decoded.SlopeDegrees);
        Assert.Equal(RoofRidgeEdgeFamily.SourceEdge01, decoded.RidgeEdgeFamily);
        Assert.Equal(data.RigidFootprint, decoded.RigidFootprint);
        Assert.Null(decoded.RidgeDirectionX);
        Assert.Null(decoded.RidgeDirectionY);
        Assert.Null(decoded.FootprintSignature);
        Assert.DoesNotContain(',', payload);
        Assert.DoesNotContain(';', payload);
    }

    [Fact]
    public void SchemaV2_PreservesSlopeRoundTripPrecision()
    {
        const double slope = 33.123456789012345d;
        var decoded = Decode(RoofDefinitionDataCodec.Encode(
            Create(Rectangle(), slope, RoofRidgeEdgeFamily.SourceEdge12)));
        Assert.Equal(slope, decoded.SlopeDegrees);
    }

    [Theory]
    [InlineData(RoofRidgeEdgeFamily.SourceEdge01)]
    [InlineData(RoofRidgeEdgeFamily.SourceEdge12)]
    public void SchemaV2_PreservesRidgeEdgeFamily(RoofRidgeEdgeFamily family)
    {
        var decoded = Decode(RoofDefinitionDataCodec.Encode(Create(Rectangle(), 35d, family)));
        Assert.Equal(family, decoded.RidgeEdgeFamily);
    }

    [Theory]
    [InlineData(false, RoofPolygonOrientation.CounterClockwise)]
    [InlineData(true, RoofPolygonOrientation.Clockwise)]
    public void Descriptor_PreservesNativeOrientation(
        bool clockwise,
        RoofPolygonOrientation expected)
    {
        var data = Create(Rectangle(clockwise: clockwise), 35d, RoofRidgeEdgeFamily.SourceEdge01);
        Assert.Equal(expected, data.RigidFootprint!.SourceOrientation);
    }

    [Fact]
    public void Descriptor_PreservesNativeEdgeFamilyDimensions()
    {
        var descriptor = Create(Rectangle(12345.6789d, 4567.8901d), 35d,
            RoofRidgeEdgeFamily.SourceEdge01).RigidFootprint!;
        Assert.Equal(4, descriptor.VertexCount);
        Assert.Equal(12345.6789d, descriptor.Edge01LengthMm);
        Assert.Equal(4567.8901d, descriptor.Edge12LengthMm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2|SimpleGable|35")]
    [InlineData("2|SimpleGable|35|Edge01|4|CCW|10000")]
    [InlineData("2|SimpleGable|35|Edge01|4|CCW|10000|6000|extra")]
    public void MalformedOrTruncatedV2_IsRejected(string payload)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.MalformedPayload, error);
    }

    [Fact]
    public void FutureSchema_IsRejectedSafely()
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(
            "4|SimpleGable|35|Edge01|4|CCW|10000|6000",
            out _,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedFutureSchema, error);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Edge10")]
    [InlineData("")]
    public void InvalidRidgeSelector_IsRejected(string selector)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(
            $"2|SimpleGable|35|{selector}|4|CCW|10000|6000",
            out _,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily, error);
    }

    [Theory]
    [InlineData("NaN", "6000")]
    [InlineData("Infinity", "6000")]
    [InlineData("10000", "-Infinity")]
    [InlineData("0", "6000")]
    [InlineData("10000", "0.001")]
    public void InvalidDescriptorDimensions_AreRejected(string first, string second)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(
            $"2|SimpleGable|35|Edge01|4|CCW|{first}|{second}",
            out _,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor, error);
    }

    [Theory]
    [InlineData("3", "CCW")]
    [InlineData("5", "CCW")]
    [InlineData("4", "Undefined")]
    [InlineData("4", "0")]
    public void InvalidTopologyDescriptor_IsRejected(string count, string orientation)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(
            $"2|SimpleGable|35|Edge01|{count}|{orientation}|10000|6000",
            out _,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor, error);
    }

    [Fact]
    public void ValidV1_UnchangedGeometryStillRestores()
    {
        var source = Rectangle();
        var footprint = Validate(source);
        var geometry = Solve(source, footprint, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var legacy = LegacyData(footprint, geometry);

        var restored = RoofDefinitionPersistence.Restore(
            source,
            footprint,
            Decode(RoofDefinitionDataCodec.Encode(legacy)));

        Assert.True(restored.IsValid);
        Assert.Equal(geometry.Signature, restored.Geometry!.Signature);
    }

    [Fact]
    public void V1Payload_RemainsByteForByteSemanticallyStable()
    {
        const string payload = "1|SimpleGable|35.25|1|0|0,0;10000,0;10000,6000;0,6000";
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(Decode(payload)));
    }

    [Fact]
    public void V1Read_DoesNotNormalizeToV2()
    {
        var decoded = Decode("1|SimpleGable|35|1|0|0,0;10000,0;10000,6000;0,6000");
        Assert.Equal(1, decoded.SchemaVersion);
        Assert.Null(decoded.RigidFootprint);
        Assert.Null(decoded.RidgeEdgeFamily);
    }

    [Fact]
    public void V1TranslatedGeometry_RemainsStale()
    {
        var original = Rectangle();
        var footprint = Validate(original);
        var legacy = LegacyData(
            footprint,
            Solve(original, footprint, 35d, RoofRidgeEdgeFamily.SourceEdge01));
        var moved = Transform(original, 0d, 500d, -900d);
        var restored = RoofDefinitionPersistence.Restore(moved, Validate(moved), legacy);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, restored.Error);
    }

    [Fact]
    public void V2Translation_RestoresAtTranslatedLocation()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var before = Restore(original, data);
        var moved = Transform(original, 0d, 1234.5d, -987.25d);
        var after = Restore(moved, data);

        AssertPointTranslated(before.Ridge.Start, after.Ridge.Start, 1234.5d, -987.25d);
        AssertPointTranslated(before.Ridge.End, after.Ridge.End, 1234.5d, -987.25d);
        Assert.Equal(before.SlopeDegrees, after.SlopeDegrees);
        AssertParallel(after.RidgeDirection, EdgeDirection(moved, 0));
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(90d)]
    [InlineData(-17d)]
    public void V2Rotation_RestoresAndRotatesRidge(double angleDegrees)
    {
        var original = Rectangle();
        var data = Create(original, 33d, RoofRidgeEdgeFamily.SourceEdge01);
        var rotated = Transform(original, angleDegrees, 0d, 0d);
        var geometry = Restore(rotated, data);

        AssertParallel(geometry.RidgeDirection, EdgeDirection(rotated, 0));
        Assert.Equal(33d, geometry.SlopeDegrees);
        AssertFinite(geometry);
    }

    [Fact]
    public void ClockwiseSource_RotationKeepsNativeFamily()
    {
        var original = Rectangle(clockwise: true);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge12);
        var rotated = Transform(original, 41d, 700d, -300d);
        AssertParallel(Restore(rotated, data).RidgeDirection, EdgeDirection(rotated, 1));
    }

    [Fact]
    public void SquareRotation90_PreservesSelectedEdge01Family()
    {
        var square = Rectangle(8000d, 8000d);
        var data = Create(square, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var rotated = Transform(square, 90d, 400d, 500d);
        var restored = Restore(rotated, data);

        AssertParallel(restored.RidgeDirection, EdgeDirection(rotated, 0));
        AssertPerpendicular(restored.RidgeDirection, EdgeDirection(rotated, 1));
    }

    [Fact]
    public void SquareRotation90_PreservesSelectedEdge12Family()
    {
        var square = Rectangle(8000d, 8000d);
        var data = Create(square, 35d, RoofRidgeEdgeFamily.SourceEdge12);
        var rotated = Transform(square, 90d, 400d, 500d);
        var restored = Restore(rotated, data);

        AssertParallel(restored.RidgeDirection, EdgeDirection(rotated, 1));
        AssertPerpendicular(restored.RidgeDirection, EdgeDirection(rotated, 0));
    }

    [Fact]
    public void RepeatedMoveRotate_RestoresDeterministicallyWithoutPayloadMutation()
    {
        var original = Rectangle();
        var data = Create(original, 31.75d, RoofRidgeEdgeFamily.SourceEdge12);
        var payload = RoofDefinitionDataCodec.Encode(data);
        var transformed = Transform(original, 30d, 1000d, -2000d);
        transformed = Transform(transformed, -17d, 350d, 725d);

        var first = Restore(transformed, data);
        var second = Restore(transformed, data);
        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(data));
    }

    [Fact]
    public void RotationFloatingPointResidue_IsAccepted()
    {
        var original = Rectangle(12345.6789d, 6789.1234d);
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var transformed = Transform(original, 29.999999999d, 1e8d, -1e8d);
        Assert.True(RestoreResult(transformed, data).IsValid);
    }

    [Theory]
    [InlineData(100d, 0d)]
    [InlineData(0d, 100d)]
    [InlineData(-250d, 75d)]
    public void ChangedEdgeDimensions_AreSupportedRectangularResize(double widthChange, double heightChange)
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var changed = Rectangle(10000d + widthChange, 6000d + heightChange);
        var result = RestoreResult(changed, data);
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(
            RoofSourceChangeKind.SupportedResize,
            RoofDefinitionPersistence.Classify(changed, Validate(changed), data).Kind);
        AssertParallel(result.Geometry!.RidgeDirection, EdgeDirection(changed, 0));
        Assert.Equal(35d, result.Geometry.SlopeDegrees);
    }

    [Fact]
    public void StretchLikeOneSideChange_RestoresPreservedRidgeFamily()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var stretched = Input([
            new(0d, 0d), new(10250d, 0d), new(10250d, 6000d), new(0d, 6000d)]);
        var result = RestoreResult(stretched, data);
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(RoofSourceChangeKind.SupportedResize,
            RoofDefinitionPersistence.Classify(stretched, Validate(stretched), data).Kind);
        AssertParallel(result.Geometry!.RidgeDirection, EdgeDirection(stretched, 0));
    }

    [Fact]
    public void SkewedCurrentSource_DoesNotProducePersistedPreview()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge01);
        var skewed = Input([
            new(0d, 0d), new(10000d, 0d), new(10500d, 6000d), new(500d, 6000d)]);
        var result = RestoreResult(skewed, data);
        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
    }

    [Fact]
    public void CopiedOwnerAtTranslatedPosition_RestoresIndependently()
    {
        var original = Rectangle();
        var data = Create(original, 35d, RoofRidgeEdgeFamily.SourceEdge12);
        var copy = Transform(original, 0d, 25000d, 12000d);
        var originalGeometry = Restore(original, data);
        var copiedGeometry = Restore(copy, data);

        Assert.NotEqual(originalGeometry.Signature, copiedGeometry.Signature);
        AssertPointTranslated(
            originalGeometry.Ridge.Start,
            copiedGeometry.Ridge.Start,
            25000d,
            12000d);
    }

    [Fact]
    public void IdenticalCurrentSource_RepeatedRestoreIsIdenticalAndFinite()
    {
        var current = Transform(Rectangle(), 53d, -1200d, 7400d);
        var data = Create(Rectangle(), 38.125d, RoofRidgeEdgeFamily.SourceEdge01);
        var first = Restore(current, data);
        var second = Restore(current, data);
        Assert.Equal(first.Signature, second.Signature);
        AssertFinite(first);
    }

    private static RoofDefinitionData Create(
        RoofFootprintInput source,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var footprint = Validate(source);
        return RoofDefinitionPersistence.Create(
            source,
            footprint,
            Solve(source, footprint, slope, family));
    }

    private static RoofDefinitionData LegacyData(
        RoofFootprint footprint,
        SimpleGableRoofGeometry geometry) => new(
            RoofDefinitionDataSchema.LegacyAbsoluteVersion,
            RoofKind.SimpleGable,
            geometry.SlopeDegrees,
            geometry.RidgeDirection.X,
            geometry.RidgeDirection.Y,
            footprint.Signature);

    private static SimpleGableRoofGeometry Restore(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var result = RestoreResult(source, data);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofDefinitionRestoreResult RestoreResult(
        RoofFootprintInput source,
        RoofDefinitionData data)
    {
        var validation = RoofFootprintValidator.Validate(source);
        if (!validation.IsValid || validation.Footprint is null)
        {
            return new RoofDefinitionRestoreResult(
                false,
                null,
                RoofDefinitionRestoreError.InvalidDefinition);
        }

        return RoofDefinitionPersistence.Restore(source, validation.Footprint, data);
    }

    private static SimpleGableRoofGeometry Solve(
        RoofFootprintInput source,
        RoofFootprint footprint,
        double slope,
        RoofRidgeEdgeFamily family)
    {
        var direction = EdgeDirection(source, family == RoofRidgeEdgeFamily.SourceEdge01 ? 0 : 1);
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofDirection2D EdgeDirection(RoofFootprintInput source, int edgeIndex)
    {
        var vertices = source.Vertices!;
        var first = vertices[edgeIndex];
        var second = vertices[(edgeIndex + 1) % vertices.Count];
        Assert.True(RoofDirection2D.TryCreate(second.X - first.X, second.Y - first.Y, out var direction));
        return direction;
    }

    private static RoofDefinitionData Decode(string payload)
    {
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error), error.ToString());
        return Assert.IsType<RoofDefinitionData>(data);
    }

    private static RoofFootprint Validate(RoofFootprintInput source)
    {
        var result = RoofFootprintValidator.Validate(source);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }

    private static RoofFootprintInput Rectangle(
        double width = 10000d,
        double height = 6000d,
        bool clockwise = false)
    {
        var vertices = new[]
        {
            new RoofPoint2D(1000d, -2000d),
            new RoofPoint2D(1000d + width, -2000d),
            new RoofPoint2D(1000d + width, -2000d + height),
            new RoofPoint2D(1000d, -2000d + height),
        };
        if (clockwise)
        {
            Array.Reverse(vertices);
        }
        return Input(vertices);
    }

    private static RoofFootprintInput Transform(
        RoofFootprintInput source,
        double angleDegrees,
        double translateX,
        double translateY)
    {
        var radians = angleDegrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return Input(source.Vertices!.Select(point => new RoofPoint2D(
            point.X * cosine - point.Y * sine + translateX,
            point.X * sine + point.Y * cosine + translateY)).ToArray());
    }

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> vertices) =>
        new(vertices, true, false, true);

    private static void AssertPointTranslated(
        RoofPoint3D before,
        RoofPoint3D after,
        double x,
        double y)
    {
        Assert.Equal(before.X + x, after.X, 7);
        Assert.Equal(before.Y + y, after.Y, 7);
        Assert.Equal(before.Z, after.Z, 7);
    }

    private static void AssertParallel(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.Y - first.Y * second.X) < 1e-9d);

    private static void AssertPerpendicular(RoofDirection2D first, RoofDirection2D second) =>
        Assert.True(Math.Abs(first.X * second.X + first.Y * second.Y) < 1e-9d);

    private static void AssertFinite(SimpleGableRoofGeometry geometry)
    {
        var values = new[]
        {
            geometry.Ridge.Start.X, geometry.Ridge.Start.Y, geometry.Ridge.Start.Z,
            geometry.Ridge.End.X, geometry.Ridge.End.Y, geometry.Ridge.End.Z,
            geometry.RunMm, geometry.RiseMm, geometry.SlopeDegrees,
        };
        Assert.All(values, value => Assert.True(double.IsFinite(value)));
    }
}
