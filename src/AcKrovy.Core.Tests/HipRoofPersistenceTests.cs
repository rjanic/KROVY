using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class HipRoofPersistenceTests
{
    public static IEnumerable<object[]> SupportedFootprints()
    {
        yield return ["rectangle", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }];
        yield return ["L", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(8000, 3000), new(3000, 3000), new(3000, 8000), new(0, 8000) }];
        yield return ["U", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000), new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000) }];
        yield return ["T", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000), new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000) }];
    }

    [Fact]
    public void RoofKindValuesAndSchemaRemainStable()
    {
        Assert.Equal(1, (int)RoofKind.SimpleGable);
        Assert.Equal(2, (int)RoofKind.AsymmetricGable);
        Assert.Equal(3, (int)RoofKind.Monopitch);
        Assert.Equal(4, (int)RoofKind.Hip);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
    }

    [Theory]
    [InlineData("5|SimpleGable|30|30|0|Edge01|4|CCW|10000|6000|Locked|")]
    [InlineData("5|AsymmetricGable|20|35|450|Edge01|4|CCW|10000|6000|Locked|")]
    [InlineData("5|Monopitch|30|30|3464.1016151377544|Edge12|4|CCW|10000|6000|Locked|")]
    public void ExistingCurrentSchemaTokensRemainByteForByteStable(string payload)
    {
        var data = Decode(payload);
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(data));
    }

    [Fact]
    public void HipUsesExplicitStableTokenAndNoRidgeDirectionOrEdgeFamily()
    {
        const string payload = "5|Hip|30|30|0|None|4|CCW|10000|6000|Locked|";

        var data = Decode(payload);

        Assert.Equal(RoofKind.Hip, data.Kind);
        Assert.Equal(30d, data.SlopeDegrees);
        Assert.Equal(30d, data.EffectiveFace1SlopeDegrees);
        Assert.Null(data.RidgeDirectionX);
        Assert.Null(data.RidgeDirectionY);
        Assert.Null(data.RidgeEdgeFamily);
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(data));
    }

    [Theory]
    [MemberData(nameof(SupportedFootprints))]
    public void PersistDecodeReload_ReconstructsEquivalentTopology(
        string name,
        RoofPoint2D[] points)
    {
        var input = new RoofFootprintInput(points, true);
        var footprint = Validate(input);
        var original = Solve(footprint, 37.25d);

        var created = RoofDefinitionPersistence.Create(input, footprint, original);
        var decoded = Decode(RoofDefinitionDataCodec.Encode(created));
        var restored = RoofDefinitionPersistence.Restore(input, footprint, decoded);

        Assert.Equal(RoofKind.Hip, decoded.Kind);
        Assert.Equal(37.25d, decoded.SlopeDegrees);
        Assert.Null(decoded.RidgeEdgeFamily);
        Assert.True(restored.IsValid, name);
        var reloaded = Assert.IsType<HipRoofGeometry>(restored.Geometry);
        Assert.Equal(original.Topology.Signature, reloaded.Topology.Signature);
        Assert.Equal(original.Signature, reloaded.Signature);
    }

    [Theory]
    [InlineData("5|Hip|30|35|0|None|4|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidSlope)]
    [InlineData("5|Hip|30|30|1|None|4|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidEaveHeightDifference)]
    [InlineData("5|Hip|30|30|0|Edge01|4|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily)]
    [InlineData("5|Hip|30|30|0|None|2|CCW|10000|6000|Locked|", RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor)]
    [InlineData("5|Hip|30|30|0|None|4|CCW|10000|6000|Unlocked|", RoofDefinitionDataDecodeError.InvalidEditState)]
    public void MalformedHipPayloadsFailDeterministically(
        string payload,
        RoofDefinitionDataDecodeError expected)
    {
        Assert.False(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error));
        Assert.Null(data);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void UnknownTokenAndHipOnOldSchemaRemainRejected()
    {
        const string unknown = "5|FutureRoof|30|30|0|None|4|CCW|10000|6000|Locked|";
        Assert.False(RoofDefinitionDataCodec.TryDecode(unknown, out _, out var unknownError));
        Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedRoofKind, unknownError);

        for (var schema = 1; schema < RoofDefinitionDataSchema.CurrentVersion; schema++)
        {
            var data = new RoofDefinitionData(schema, RoofKind.Hip, 30d);
            Assert.False(RoofDefinitionDataCodec.TryValidate(data, out var error));
            Assert.Equal(RoofDefinitionDataDecodeError.UnsupportedRoofKind, error);
        }
    }

    [Fact]
    public void ChangedSourceGuardRejectsHipReloadWithoutInventingLifecycleSupport()
    {
        var originalInput = new RoofFootprintInput(
            (RoofPoint2D[])SupportedFootprints().First(item => (string)item[0] == "L")[1],
            true);
        var original = Validate(originalInput);
        var data = RoofDefinitionPersistence.Create(originalInput, original, Solve(original, 30d));
        var changedInput = new RoofFootprintInput([
            new(0, 0), new(9000, 0), new(9000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        ], true);
        var changed = Validate(changedInput);

        var restored = RoofDefinitionPersistence.Restore(changedInput, changed, data);

        Assert.False(restored.IsValid);
        Assert.Equal(RoofDefinitionRestoreError.StaleFootprint, restored.Error);
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint, double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var result = RoofFootprintValidator.Validate(input);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }

    private static RoofDefinitionData Decode(string payload)
    {
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var data, out var error), error.ToString());
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        return Assert.IsType<RoofDefinitionData>(data);
    }
}
