using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualReanchorRulesTests
{
    private static readonly RoofGeneratedMemberKey Current =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 13);

    // Rafters run along +X; stations are spaced 1000 mm along +Y, which is the
    // lateral/station direction (the anchor basis V axis).
    private static RoofReanchorCandidate Station(int index, double y) =>
        new(
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, index),
            new RoofPoint3D(0d, y, 0d),
            new RoofPoint3D(1000d, y, 0d));

    private static readonly RoofPoint3D ChildStart = new(0d, 2400d, 0d);
    private static readonly RoofPoint3D ChildEnd = new(1000d, 2400d, 0d);

    [Fact]
    public void SelectNearestAnchor_ChoosesGeometricallyNearestStation()
    {
        var candidates = new List<RoofReanchorCandidate>
        {
            Station(0, 0d),
            Station(1, 1000d),
            Station(2, 2000d),
            Station(3, 3000d),
        };

        var selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            Current, candidates, ChildStart, ChildEnd);

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestAnchor_TieBreakPrefersLowerStationIndex()
    {
        var candidates = new List<RoofReanchorCandidate>
        {
            Station(2, 2000d),
            Station(3, 3000d),
        };

        // Child sits exactly between s2 and s3 (Y=2500): effectively equidistant.
        var selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            Current,
            candidates,
            new RoofPoint3D(0d, 2500d, 0d),
            new RoofPoint3D(1000d, 2500d, 0d));

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestAnchor_FiltersDifferentRoofFace()
    {
        // A Face1 station is physically nearer but is incompatible.
        var candidates = new List<RoofReanchorCandidate>
        {
            new(
                new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 0),
                new RoofPoint3D(0d, 2390d, 0d),
                new RoofPoint3D(1000d, 2390d, 0d)),
            Station(2, 2000d),
            Station(3, 3000d),
        };

        var selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            Current, candidates, ChildStart, ChildEnd);

        Assert.NotNull(selected);
        Assert.Equal(RafterRoofFace.Face0, selected!.Key.RoofFace);
        Assert.Equal(2, selected.Key.StationIndex);
    }

    [Fact]
    public void SelectNearestAnchor_PreservesExactChildGeometryViaRelativeRoundTrip()
    {
        var candidates = new List<RoofReanchorCandidate>
        {
            Station(2, 2000d),
            Station(3, 3000d),
        };

        var selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            Current, candidates, ChildStart, ChildEnd);
        Assert.NotNull(selected);

        // Recompute the RelativeSegment against the NEW anchor, then replay it: the
        // child must land back at its exact moved WCS position (no snap).
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            selected!.Start, selected.End, ChildStart, ChildEnd, out var relative));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            selected.Start, selected.End, relative, out var replayedStart, out var replayedEnd));

        Assert.Equal(ChildStart.X, replayedStart.X, 6);
        Assert.Equal(ChildStart.Y, replayedStart.Y, 6);
        Assert.Equal(ChildStart.Z, replayedStart.Z, 6);
        Assert.Equal(ChildEnd.X, replayedEnd.X, 6);
        Assert.Equal(ChildEnd.Y, replayedEnd.Y, 6);
        Assert.Equal(ChildEnd.Z, replayedEnd.Z, 6);
    }

    [Fact]
    public void SelectNearestAnchor_NoCompatibleCandidate_ReturnsNull()
    {
        var candidates = new List<RoofReanchorCandidate>
        {
            // Only Face1 candidates; incompatible with a Face0 current anchor.
            new(
                new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 0),
                new RoofPoint3D(0d, 0d, 0d),
                new RoofPoint3D(1000d, 0d, 0d)),
        };

        var selected = RoofAttachedManualReanchorRules.SelectNearestAnchor(
            Current, candidates, ChildStart, ChildEnd);

        Assert.Null(selected);
    }
}
