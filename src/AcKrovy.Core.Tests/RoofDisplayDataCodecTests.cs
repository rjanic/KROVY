using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayDataCodecTests
{
    [Theory]
    [InlineData(RoofDisplayEdgeRole.Ridge)]
    [InlineData(RoofDisplayEdgeRole.Eave0)]
    [InlineData(RoofDisplayEdgeRole.Eave1)]
    [InlineData(RoofDisplayEdgeRole.GableSlope00)]
    [InlineData(RoofDisplayEdgeRole.GableSlope01)]
    [InlineData(RoofDisplayEdgeRole.GableSlope10)]
    [InlineData(RoofDisplayEdgeRole.GableSlope11)]
    public void CurrentSchema_RoundTripsEveryStableRole(RoofDisplayEdgeRole role)
    {
        var source = new RoofDisplayData(1, "2AF", role, "generation-signature");

        var encoded = RoofDisplayDataCodec.Encode(source);

        Assert.True(RoofDisplayDataCodec.TryDecode(encoded, out var decoded, out var error));
        Assert.Equal(RoofDisplayDataDecodeError.None, error);
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void FutureSchema_IsRejectedWithoutFallback()
    {
        Assert.False(RoofDisplayDataCodec.TryDecode(
            "2|2AF|Ridge|signature",
            out var decoded,
            out var error));
        Assert.Null(decoded);
        Assert.Equal(RoofDisplayDataDecodeError.UnsupportedFutureSchema, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1||Ridge|signature")]
    [InlineData("1|2AF|Unknown|signature")]
    [InlineData("1|2AF|Ridge|")]
    [InlineData("1|2AF|Ridge|signature|extra")]
    public void MalformedPayload_IsRejected(string payload)
    {
        Assert.False(RoofDisplayDataCodec.TryDecode(payload, out _, out _));
    }

    [Fact]
    public void PartialOwnerRead_SurvivesMalformedRoleForSafeRebuildOwnership()
    {
        Assert.True(RoofDisplayDataCodec.TryReadOwnerReference(
            "1|2AF|BrokenRole|signature",
            out var owner));
        Assert.Equal("2AF", owner);
    }

    [Fact]
    public void TechnicalTokens_AreInvariantAndCompact()
    {
        var encoded = RoofDisplayDataCodec.Encode(new RoofDisplayData(
            RoofDisplayDataSchema.CurrentVersion,
            "ABC",
            RoofDisplayEdgeRole.GableSlope11,
            "1;2;3"));

        Assert.Equal("1|ABC|GableSlope11|1;2;3", encoded);
    }
}
