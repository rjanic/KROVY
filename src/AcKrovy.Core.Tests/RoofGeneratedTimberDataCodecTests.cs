using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedTimberDataCodecTests
{
    [Theory]
    [InlineData(RafterRoofFace.Face0)]
    [InlineData(RafterRoofFace.Face1)]
    public void CurrentSchemaRoundTripsBothFaces(RafterRoofFace face)
    {
        var source = Sample(face);

        var encoded = RoofGeneratedTimberDataCodec.Encode(source);

        Assert.True(RoofGeneratedTimberDataCodec.TryDecode(encoded, out var decoded, out var error));
        Assert.Equal(RoofGeneratedTimberDataDecodeError.None, error);
        Assert.Equal(source, decoded);
        Assert.Contains("833.3333333333334", encoded);
    }

    [Fact]
    public void EncodingIsInvariantAcrossCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            Assert.Contains("833.3333333333334", RoofGeneratedTimberDataCodec.Encode(Sample()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FutureSchemaIsRejectedSpecifically()
    {
        var payload = RoofGeneratedTimberDataCodec.Encode(Sample());
        payload = "2" + payload.Substring(1);

        Assert.False(RoofGeneratedTimberDataCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(RoofGeneratedTimberDataDecodeError.UnsupportedFutureSchema, error);
    }

    [Theory]
    [InlineData(-1, 13)]
    [InlineData(13, 13)]
    [InlineData(0, 1)]
    public void InvalidStationIsRejected(int index, int count)
    {
        var data = Sample() with { StationIndex = index, StationCount = count };

        Assert.False(RoofGeneratedTimberDataCodec.TryValidate(data, out var error));
        Assert.Equal(RoofGeneratedTimberDataDecodeError.InvalidStation, error);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double spacing)
    {
        var data = Sample() with { RequestedMaximumSpacingMm = spacing };

        Assert.False(RoofGeneratedTimberDataCodec.TryValidate(data, out var error));
        Assert.Equal(RoofGeneratedTimberDataDecodeError.InvalidMaximumSpacing, error);
    }

    [Fact]
    public void SchemaContractIsIndependentFromTimberSchema()
    {
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(7, AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion);
    }

    private static RoofGeneratedTimberData Sample(RafterRoofFace face = RafterRoofFace.Face0) =>
        new(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            "2AF",
            RoofGeneratedTimberKind.Rafter,
            face,
            4,
            13,
            833.3333333333334,
            "RAFTER_LAYOUT_V1;signature");
}
