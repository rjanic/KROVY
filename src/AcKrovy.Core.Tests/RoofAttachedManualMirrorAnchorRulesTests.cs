using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualMirrorAnchorRulesTests
{
    // Ridge runs along +X; Face0 eave at Y=-1000, Face1 eave at Y=+1000, ridge at Y=0.
    // A rafter spans eave → ridge (the Y direction); stations are spaced along X.
    private static RoofReanchorCandidate Rafter(RafterRoofFace face, int station, double x) =>
        new(
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, face, station),
            new RoofPoint3D(x, face == RafterRoofFace.Face0 ? -1000d : 1000d, 0d),
            new RoofPoint3D(x, 0d, 0d));

    private static readonly IReadOnlyList<RoofReanchorCandidate> Candidates = new[]
    {
        Rafter(RafterRoofFace.Face0, 0, 0d),
        Rafter(RafterRoofFace.Face0, 1, 1000d),
        Rafter(RafterRoofFace.Face0, 2, 2000d),
        Rafter(RafterRoofFace.Face1, 0, 0d),
        Rafter(RafterRoofFace.Face1, 1, 1000d),
        Rafter(RafterRoofFace.Face1, 2, 2000d),
    };

    [Fact]
    public void SelectNearestMirrorAnchor_SelectsNearestSameFaceStation()
    {
        var childStart = new RoofPoint3D(400d, -1000d, 0d);
        var childEnd = new RoofPoint3D(400d, 0d, 0d);

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            RoofGeneratedTimberKind.Rafter, Candidates, childStart, childEnd);

        Assert.NotNull(selected);
        Assert.Equal(RafterRoofFace.Face0, selected!.Key.RoofFace);
        Assert.Equal(0, selected.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestMirrorAnchor_MirroredAcrossRidge_SelectsOppositeFaceStation()
    {
        // Face0 s1 mirrored across the ridge (Y=0) becomes a Face1 rafter at X=1000.
        var childStart = new RoofPoint3D(1000d, 1000d, 0d);
        var childEnd = new RoofPoint3D(1000d, 0d, 0d);

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            RoofGeneratedTimberKind.Rafter, Candidates, childStart, childEnd);

        Assert.NotNull(selected);
        Assert.Equal(RafterRoofFace.Face1, selected!.Key.RoofFace);
        Assert.Equal(1, selected.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestMirrorAnchor_TieBreakPrefersLowerStationIndex()
    {
        var childStart = new RoofPoint3D(500d, -1000d, 0d);
        var childEnd = new RoofPoint3D(500d, 0d, 0d);

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            RoofGeneratedTimberKind.Rafter, Candidates, childStart, childEnd);

        Assert.NotNull(selected);
        Assert.Equal(0, selected!.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestMirrorAnchor_NonRafterOrientation_ReturnsNull()
    {
        // A line perpendicular to the rafter span has U1 == U0 for every candidate,
        // so no compatible anchor exists.
        var childStart = new RoofPoint3D(500d, -1000d, 0d);
        var childEnd = new RoofPoint3D(1500d, -1000d, 0d);

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            RoofGeneratedTimberKind.Rafter, Candidates, childStart, childEnd);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectNearestMirrorAnchor_PreservesExactGeometryViaRoundTrip()
    {
        var childStart = new RoofPoint3D(400d, -1000d, 0d);
        var childEnd = new RoofPoint3D(400d, 0d, 0d);

        var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
            RoofGeneratedTimberKind.Rafter, Candidates, childStart, childEnd);
        Assert.NotNull(selected);

        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            selected!.Start, selected.End, childStart, childEnd, out var relative));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            selected.Start, selected.End, relative, out var replayedStart, out var replayedEnd));

        Assert.Equal(childStart.X, replayedStart.X, 6);
        Assert.Equal(childStart.Y, replayedStart.Y, 6);
        Assert.Equal(childStart.Z, replayedStart.Z, 6);
        Assert.Equal(childEnd.X, replayedEnd.X, 6);
        Assert.Equal(childEnd.Y, replayedEnd.Y, 6);
        Assert.Equal(childEnd.Z, replayedEnd.Z, 6);
    }
}
