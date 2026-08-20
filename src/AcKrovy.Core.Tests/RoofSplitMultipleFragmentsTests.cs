using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSplitMultipleFragmentsTests
{
    [Fact]
    public void TwoSplitFragments_SameAnchor_ReplayIndependently_DistinctNonOverlapping()
    {
        // Two BREAK fragments on one logical Generated member share the SAME anchor key K
        // but keep INDEPENDENT ChildIdentity + RelativeSegment, so they replay to distinct,
        // non-overlapping positions against the rebuilt anchor.
        var anchorStart = new RoofPoint3D(0d, 0d, 0d);
        var anchorEnd = new RoofPoint3D(4000d, 0d, 0d);

        var fragAStart = new RoofPoint3D(1000d, 200d, 0d);
        var fragAEnd = new RoofPoint3D(1500d, 200d, 0d);
        var fragBStart = new RoofPoint3D(2500d, -200d, 0d);
        var fragBEnd = new RoofPoint3D(3000d, -200d, 0d);

        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            anchorStart, anchorEnd, fragAStart, fragAEnd, out var relA));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            anchorStart, anchorEnd, fragBStart, fragBEnd, out var relB));

        // Distinct relative segments (they are different fragments).
        Assert.NotEqual(relA, relB);

        // Rebuild the anchor (roof moved), then replay both against it.
        var rebuiltStart = new RoofPoint3D(0d, 1000d, 0d);
        var rebuiltEnd = new RoofPoint3D(4000d, 1000d, 0d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            rebuiltStart, rebuiltEnd, relA, out var aStart, out var aEnd));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            rebuiltStart, rebuiltEnd, relB, out var bStart, out var bEnd));

        // Both fragments remain separated and at their persisted relative offsets.
        Assert.True(aStart.X < aEnd.X, "fragment A must not collapse");
        Assert.True(bStart.X < bEnd.X, "fragment B must not collapse");
        Assert.True(aEnd.X < bStart.X, "fragment A and B must not overlap along the span");
        Assert.Equal(1200d, aStart.Y, 3);
        Assert.Equal(800d, bStart.Y, 3);
    }

    [Fact]
    public void SplitFragments_SurviveShrinkExpand_WithExactPersistedOffset()
    {
        // A split fragment captured on the full member must replay against the rebuilt
        // (shorter) anchor at its EXACT persisted relative offset, and again correctly
        // after the member expands — never reconstructed from canonical unsplit geometry.
        var fullStart = new RoofPoint3D(0d, 0d, 0d);
        var fullEnd = new RoofPoint3D(4000d, 0d, 0d);
        var fragStart = new RoofPoint3D(3000d, 50d, 0d);
        var fragEnd = new RoofPoint3D(3600d, 50d, 0d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            fullStart, fullEnd, fragStart, fragEnd, out var relative));

        // Shrink: anchor shortens (roof smaller), K station still exists.
        var shrunkStart = new RoofPoint3D(0d, 0d, 0d);
        var shrunkEnd = new RoofPoint3D(3800d, 0d, 0d);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            shrunkStart, shrunkEnd, relative, out var shrinkStart, out var shrinkEnd));

        // Expand back to full — the persisted relative offset is authoritative.
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            fullStart, fullEnd, relative, out var expandStart, out var expandEnd));

        Assert.Equal(fragStart.X, expandStart.X, 3);
        Assert.Equal(fragEnd.X, expandEnd.X, 3);
        Assert.Equal(fragStart.Y, expandStart.Y, 3);
    }
}
