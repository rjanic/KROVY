using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualTimberDataCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrip_AttachedManualRole()
    {
        var data = new RoofAttachedManualTimberData(
            1,
            "291A",
            "299E",
            RoofTimberChildRole.AttachedManual);

        var encoded = RoofAttachedManualTimberDataCodec.Encode(data);
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(encoded, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Decode_RejectsGeneratedRoleMarker()
    {
        var payload = "1|291A|299E|Generated";
        Assert.False(RoofAttachedManualTimberDataCodec.TryDecode(payload, out _));
    }

    [Fact]
    public void EncodeDecode_V3_RoundTrip_CopyOrigin()
    {
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            7);
        var relative = new RoofAttachedManualRelativeSegment(100d, 50d, 0d, 3000d, 50d, 0d);
        var data = new RoofAttachedManualTimberData(
            3,
            "291A",
            "299E",
            RoofTimberChildRole.AttachedManual,
            key,
            relative,
            RoofAttachedManualOrigin.Copy);

        var encoded = RoofAttachedManualTimberDataCodec.Encode(data);
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(data, decoded);
        Assert.Equal(RoofAttachedManualOrigin.Copy, decoded!.Origin);
    }

    [Fact]
    public void Decode_V2_DefaultsOriginToSplit()
    {
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            7);
        var relative = new RoofAttachedManualRelativeSegment(100d, 50d, 0d, 3000d, 50d, 0d);
        var v2 = new RoofAttachedManualTimberData(
            2,
            "291A",
            "299E",
            RoofTimberChildRole.AttachedManual,
            key,
            relative);

        var encoded = RoofAttachedManualTimberDataCodec.Encode(v2);
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(RoofAttachedManualOrigin.Split, decoded!.Origin);
    }
}
