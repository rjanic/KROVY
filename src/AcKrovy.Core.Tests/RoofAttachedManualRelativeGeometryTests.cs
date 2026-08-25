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

    [Fact]
    public void ReversedAnchorRebase_PreservesExactPhysicalSegment()
    {
        var anchorStart = new RoofPoint3D(1700d, -230d, 40d);
        var anchorEnd = new RoofPoint3D(5300d, 2770d, 40d);
        var childStart = new RoofPoint3D(2600d, 900d, 65d);
        var childEnd = new RoofPoint3D(4600d, 2100d, 65d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            anchorStart,
            anchorEnd,
            childStart,
            childEnd,
            out var relative));

        var anchorLength = anchorStart.DistanceTo(anchorEnd);
        var rebased = RoofAttachedManualRelativeGeometryRules
            .RebaseForReversedAnchorDirection(relative, anchorLength);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            anchorEnd,
            anchorStart,
            rebased,
            out var replayStart,
            out var replayEnd));

        AssertPoint(childStart, replayStart);
        AssertPoint(childEnd, replayEnd);
    }

    [Fact]
    public void ReversedAnchorRebase_TwiceRestoresPersistedNumbersWithoutDrift()
    {
        var original = new RoofAttachedManualRelativeSegment(
            -125.5d,
            240.25d,
            18d,
            4820.75d,
            -95.5d,
            18d);

        var once = RoofAttachedManualRelativeGeometryRules
            .RebaseForReversedAnchorDirection(original, 5100d);
        var twice = RoofAttachedManualRelativeGeometryRules
            .RebaseForReversedAnchorDirection(once, 5100d);

        Assert.Equal(original, twice);
    }

    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Z, actual.Z, 8);
    }
}
