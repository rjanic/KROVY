using AcKrovy.AutoCAD.UI;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinSectionLabelLanesTests
{
    [Fact]
    public void Assign_StacksOverlappingLabelsIntoDistinctLanes()
    {
        var requests = new[]
        {
            new AutomaticPurlinSectionLabelLanes.LabelRequest("a", 100, 200, 80, 40),
            new AutomaticPurlinSectionLabelLanes.LabelRequest("b", 105, 205, 80, 40),
            new AutomaticPurlinSectionLabelLanes.LabelRequest("c", 110, 210, 80, 40),
        };

        var first = AutomaticPurlinSectionLabelLanes.Assign(requests, laneStep: 50, padding: 2);
        var second = AutomaticPurlinSectionLabelLanes.Assign(requests, laneStep: 50, padding: 2);

        Assert.Equal(first, second);
        Assert.Equal(3, first.Select(placement => placement.LaneIndex).Distinct().Count());
        Assert.DoesNotContain(
            first.SelectMany((left, index) => first.Skip(index + 1).Select(right => (left, right))),
            pair => pair.left.Bounds.Intersects(pair.right.Bounds, padding: 2));
    }

    [Fact]
    public void Assign_KeepsDistantLabelsOnNearestLane()
    {
        var requests = new[]
        {
            new AutomaticPurlinSectionLabelLanes.LabelRequest("left", 0, 100, 60, 30),
            new AutomaticPurlinSectionLabelLanes.LabelRequest("right", 400, 100, 60, 30),
        };

        var placements = AutomaticPurlinSectionLabelLanes.Assign(requests, laneStep: 40, padding: 2);

        Assert.All(placements, placement => Assert.Equal(0, placement.LaneIndex));
    }

    [Fact]
    public void Assign_IsDeterministicAcrossInputOrder()
    {
        var a = new AutomaticPurlinSectionLabelLanes.LabelRequest("a", 10, 50, 40, 20);
        var b = new AutomaticPurlinSectionLabelLanes.LabelRequest("b", 12, 52, 40, 20);
        var forward = AutomaticPurlinSectionLabelLanes.Assign([a, b], 24);
        var reverse = AutomaticPurlinSectionLabelLanes.Assign([b, a], 24);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void CompactRequestKeepsPreferredLeaderNearItsMember()
    {
        var request = AutomaticPurlinSectionLabelLanes.CreateCompactRequest(
            "member", 420d, 310d, -1, 900d, 500d, 132d,
            AutomaticPurlinRoofSectionView.LabelHeightPx, 28d,
            AutomaticPurlinRoofSectionView.LabelOffsetPx);

        Assert.Equal(
            AutomaticPurlinRoofSectionView.LabelBaseOffsetPx +
            AutomaticPurlinRoofSectionView.LabelHeightPx,
            AutomaticPurlinRoofSectionView.LabelOffsetPx);
        Assert.True(
            AutomaticPurlinSectionLabelLanes.PreferredLeaderDistance(request, 420d, 310d) <=
            AutomaticPurlinRoofSectionView.MaximumPreferredLeaderDistancePx);
        Assert.True(request.PreferredTopY >= 28d);
        Assert.True(request.AnchorX - request.Width / 2d >= 28d);
        Assert.True(
            310d - request.PreferredTopY - AutomaticPurlinRoofSectionView.LabelHeightPx >=
            AutomaticPurlinRoofSectionView.LabelHeightPx);
    }

    [Fact]
    public void RidgeCompactRequest_PrefersAnchorTwoLabelHeightsBelow()
    {
        const double anchorY = 180d;
        const double height = AutomaticPurlinRoofSectionView.LabelHeightPx;
        var request = AutomaticPurlinSectionLabelLanes.CreateCompactRequest(
            "ridge",
            450d,
            anchorY,
            0,
            900d,
            500d,
            AutomaticPurlinRoofSectionView.LabelWidthPx,
            height,
            28d,
            AutomaticPurlinRoofSectionView.LabelOffsetPx,
            preferBelow: true);

        Assert.True(request.PreferBelow);
        Assert.Equal(
            AutomaticPurlinSectionLabelLanes.RidgeLabelBelowBoxMultiples,
            2d);
        Assert.Equal(anchorY + 2d * height, request.PreferredTopY, 9);
        Assert.True(request.PreferredTopY > anchorY);
    }

    [Fact]
    public void RafterAnnotationUsesInteractiveButtonStyleFamily()
    {
        Assert.Equal(
            "InteractiveRafterAnnotationButton",
            AutomaticPurlinRoofSectionView.RafterAnnotationStyleFamily);
        Assert.Equal(3d, AutomaticPurlinRoofSectionView.RafterLabelBelowBoxMultiples);
        Assert.Equal(36d, AutomaticPurlinRoofSectionView.RafterLabelGapReferenceHeightPx);
        Assert.Equal(52d, AutomaticPurlinRoofSectionView.RafterLabelHeightPx);
        Assert.Equal(
            AutomaticPurlinRoofSectionView.RafterLabelBelowBoxMultiples *
            AutomaticPurlinRoofSectionView.RafterLabelGapReferenceHeightPx,
            AutomaticPurlinRoofSectionView.RafterLabelPreferredOffsetPx,
            9);
    }

    [Fact]
    public void RafterLabelPreferredTop_IsThreeReferenceBoxHeightsBelowLowerEdgeAnchor()
    {
        const double anchorY = 220d;
        var preferredTop =
            anchorY + AutomaticPurlinRoofSectionView.RafterLabelPreferredOffsetPx;
        Assert.Equal(
            anchorY +
            3d * AutomaticPurlinRoofSectionView.RafterLabelGapReferenceHeightPx,
            preferredTop,
            9);
        Assert.True(preferredTop > anchorY);
        // Growing the interactive 3-line button must not push the preferred top down.
        Assert.True(
            AutomaticPurlinRoofSectionView.RafterLabelHeightPx >
            AutomaticPurlinRoofSectionView.RafterLabelGapReferenceHeightPx);
    }
}
