using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualCopyIndependenceTests
{
    // Ridge along +X; Face0 eave at Y=-1000, Face1 eave at Y=+1000, ridge at Y=0.
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
    public void DistinctClonePositions_ReplayToDistinctWcsPositions()
    {
        // Three copies at three distinct lateral positions (A, B, C).
        var positions = new[] { 100d, 1100d, 2100d };
        var replayedX = new List<double>();

        foreach (var x in positions)
        {
            var start = new RoofPoint3D(x, -1000d, 0d);
            var end = new RoofPoint3D(x, 0d, 0d);

            var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
                RoofGeneratedTimberKind.Rafter, Candidates, start, end);
            Assert.NotNull(selected);

            Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
                selected!.Start, selected.End, start, end, out var relative));
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
                selected.Start, selected.End, relative, out var replayedStart, out var _));

            replayedX.Add(replayedStart.X);
        }

        // No cumulative collapse: each clone reconstructs to its own X position.
        Assert.Equal(3, replayedX.Select(x => Math.Round(x, 6)).Distinct().Count());
        Assert.Equal(100d, replayedX[0], 6);
        Assert.Equal(1100d, replayedX[1], 6);
        Assert.Equal(2100d, replayedX[2], 6);
    }

    [Fact]
    public void EachClone_SelectsItsOwnNearestAnchor()
    {
        var expected = new[] { 0, 1, 2 };
        var positions = new[] { 100d, 1100d, 2100d };

        for (var i = 0; i < positions.Length; i++)
        {
            var start = new RoofPoint3D(positions[i], -1000d, 0d);
            var end = new RoofPoint3D(positions[i], 0d, 0d);

            var selected = RoofAttachedManualReanchorRules.SelectNearestMirrorAnchor(
                RoofGeneratedTimberKind.Rafter, Candidates, start, end);

            Assert.NotNull(selected);
            Assert.Equal(expected[i], selected!.Key.StationIndex);
        }
    }
}
