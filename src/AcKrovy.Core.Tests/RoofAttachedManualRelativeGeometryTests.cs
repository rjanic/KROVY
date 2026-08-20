using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualRelativeGeometryTests
{
    [Fact]
    public void CaptureReplay_PreservesOffsetAlongAnchor()
    {
        var anchorStart = new RoofPoint3D(0d, 0d, 0d);
        var anchorEnd = new RoofPoint3D(4000d, 0d, 0d);
        var childStart = new RoofPoint3D(1000d, 200d, 0d);
        var childEnd = new RoofPoint3D(3000d, 200d, 0d);

        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            anchorStart,
            anchorEnd,
            childStart,
            childEnd,
            out var relative));

        var movedAnchorStart = new RoofPoint3D(500d, 1000d, 0d);
        var movedAnchorEnd = new RoofPoint3D(4500d, 1000d, 0d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            movedAnchorStart,
            movedAnchorEnd,
            relative,
            out var replayStart,
            out var replayEnd));

        Assert.Equal(1500d, replayStart.X, 3);
        Assert.Equal(1200d, replayStart.Y, 3);
        Assert.Equal(3500d, replayEnd.X, 3);
        Assert.Equal(1200d, replayEnd.Y, 3);
    }

    [Fact]
    public void Codec_V1_ReadsWithoutAnchor()
    {
        var payload = "1|291A|299E|AttachedManual";
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(payload, out var decoded));
        Assert.NotNull(decoded);
        Assert.Equal(1, decoded!.SchemaVersion);
        Assert.Null(decoded.AnchorGeneratedMemberKey);
        Assert.Null(decoded.RelativeSegment);
    }

    [Fact]
    public void Codec_V2_RoundTrip_AnchorAndRelative()
    {
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            7);
        var relative = new RoofAttachedManualRelativeSegment(100d, 50d, 0d, 3000d, 50d, 0d);
        var data = new RoofAttachedManualTimberData(
            2,
            "291A",
            "299E",
            RoofTimberChildRole.AttachedManual,
            key,
            relative);

        var encoded = RoofAttachedManualTimberDataCodec.Encode(data);
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(data, decoded);
    }
}
