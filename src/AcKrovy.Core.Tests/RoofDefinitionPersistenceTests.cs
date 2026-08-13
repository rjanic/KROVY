using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDefinitionPersistenceTests
{
    [Fact]
    public void CurrentSchema_RoundTripsAllFieldsAtFullPrecision()
    {
        var footprint = Rectangle();
        var geometry = Solve(footprint, 37.1234567890123d, 1d, 0d);
        var original = RoofDefinitionPersistence.Create(footprint, geometry);

        var payload = RoofDefinitionDataCodec.Encode(original);
        var decoded = Decode(payload);

        Assert.Equal(RoofDefinitionDataSchema.CurrentVersion, decoded.SchemaVersion);
        Assert.Equal(RoofKind.SimpleGable, decoded.Kind);
        Assert.Equal(original.SlopeDegrees, decoded.SlopeDegrees);
        Assert.Equal(original.RidgeDirectionX, decoded.RidgeDirectionX);
        Assert.Equal(original.RidgeDirectionY, decoded.RidgeDirectionY);
        Assert.Equal(footprint.Signature, decoded.FootprintSignature);
        Assert.Contains("37.1234567890123", payload);
    }

    [Fact]
    public void ReversedRequestedDirections_CreateEquivalentCanonicalPayloads()
    {
        var footprint = RotatedRectangle(31d);
        var forward = Solve(footprint, 42d, 0.8571673007021123d, 0.5150380749100542d);
        var reverse = Solve(footprint, 42d, -0.8571673007021123d, -0.5150380749100542d);

        var first = RoofDefinitionDataCodec.Encode(
            RoofDefinitionPersistence.Create(footprint, forward));
        var second = RoofDefinitionDataCodec.Encode(
            RoofDefinitionPersistence.Create(footprint, reverse));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1|SimpleGable|35|1|0")]
    [InlineData("1|SimpleGable|35|1|0|0,0;100,0|extra")]
    [InlineData("not-a-schema|SimpleGable|35|1|0|0,0;100,0;0,100")]
    public void MalformedOrTruncatedPayload_IsRejected(string payload)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Null(data);
        Assert.Equal(RoofDefinitionDataDecodeError.MalformedPayload, error);
    }

    [Fact]
    public void FutureSchema_IsRejectedWithoutNormalization()
    {
        const string payload = "2|SimpleGable|35|1|0|0,0;100,0;100,100;0,100|future";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedFutureSchema, error);
    }

    [Fact]
    public void UnsupportedRoofKind_IsRejected()
    {
        const string payload = "1|Hip|35|1|0|0,0;100,0;100,100;0,100";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedRoofKind, error);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0")]
    [InlineData("90")]
    public void InvalidSlope_IsRejected(string slope)
    {
        var payload = $"1|SimpleGable|{slope}|1|0|0,0;100,0;100,100;0,100";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidSlope, error);
    }

    [Theory]
    [InlineData("Infinity", "0")]
    [InlineData("0", "Infinity")]
    [InlineData("0", "0")]
    [InlineData("-1", "0")]
    [InlineData("0.5", "0")]
    public void InvalidOrNonCanonicalDirection_IsRejected(string x, string y)
    {
        var payload = $"1|SimpleGable|35|{x}|{y}|0,0;100,0;100,100;0,100";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidRidgeDirection, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("0,0;NaN,0;0,100")]
    [InlineData("0,0;100,0")]
    public void InvalidFootprintSignature_IsRejected(string signature)
    {
        var payload = $"1|SimpleGable|35|1|0|{signature}";
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.InvalidFootprintSignature, error);
    }

    [Fact]
    public void ChangedFootprint_IsDetectedAsStale()
    {
        var original = Rectangle();
        var data = RoofDefinitionPersistence.Create(original, Solve(original, 35d, 1d, 0d));
        var changed = Validate([
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(12000d, 0d),
            new RoofPoint2D(12000d, 6000d),
            new RoofPoint2D(0d, 6000d),
        ]);

        var result = RoofDefinitionPersistence.Restore(changed, data);

        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, result.Error);
    }

    [Fact]
    public void LoadedDefinition_ReSolvesEquivalentStageOneGeometry()
    {
        var footprint = RotatedRectangle(29d);
        var original = Solve(footprint, 33.75d, 0.8746197071393957d, 0.48480962024633706d);
        var decoded = Decode(RoofDefinitionDataCodec.Encode(
            RoofDefinitionPersistence.Create(footprint, original)));

        var restored = RoofDefinitionPersistence.Restore(footprint, decoded);

        Assert.True(restored.IsValid);
        Assert.NotNull(restored.Geometry);
        Assert.Equal(original.Signature, restored.Geometry.Signature);
    }

    [Fact]
    public void RepeatedEncoding_IsDeterministic()
    {
        var footprint = Rectangle();
        var data = RoofDefinitionPersistence.Create(footprint, Solve(footprint, 35d, 1d, 0d));
        Assert.Equal(
            RoofDefinitionDataCodec.Encode(data),
            RoofDefinitionDataCodec.Encode(data));
    }

    private static RoofDefinitionData Decode(string payload)
    {
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        return Assert.IsType<RoofDefinitionData>(data);
    }

    private static SimpleGableRoofGeometry Solve(
        RoofFootprint footprint,
        double slope,
        double directionX,
        double directionY)
    {
        Assert.True(RoofDirection2D.TryCreate(directionX, directionY, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid);
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Rectangle() => Validate([
        new RoofPoint2D(0d, 0d),
        new RoofPoint2D(10000d, 0d),
        new RoofPoint2D(10000d, 6000d),
        new RoofPoint2D(0d, 6000d),
    ]);

    private static RoofFootprint RotatedRectangle(double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180d;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        RoofPoint2D Rotate(double x, double y) =>
            new(2500d + x * cosine - y * sine, -1800d + x * sine + y * cosine);
        return Validate([
            Rotate(0d, 0d),
            Rotate(10000d, 0d),
            Rotate(10000d, 6000d),
            Rotate(0d, 6000d),
        ]);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> vertices)
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(
            vertices,
            true,
            false,
            true));
        Assert.True(result.IsValid);
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }
}
