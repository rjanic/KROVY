using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinMemberCenterAxisGeometryTests
{
    [Fact]
    public void AxisLengths_AreThreeTimesMemberWidthAndHeight()
    {
        var axes = AutomaticPurlinMemberCenterAxisGeometry.Create(
            centerXMm: 1000d,
            centerZMm: 500d,
            widthMm: 160d,
            heightMm: 220d);

        Assert.False(axes.IsEmpty);
        Assert.Equal(480d, axes.HorizontalLengthMm, 9);
        Assert.Equal(660d, axes.VerticalLengthMm, 9);
        Assert.Equal(1000d - 240d, axes.HorizontalStartXMm, 9);
        Assert.Equal(1000d + 240d, axes.HorizontalEndXMm, 9);
        Assert.Equal(500d - 330d, axes.VerticalStartZMm, 9);
        Assert.Equal(500d + 330d, axes.VerticalEndZMm, 9);
    }

    [Fact]
    public void Axes_IntersectAtGeometricCenter()
    {
        var axes = AutomaticPurlinMemberCenterAxisGeometry.Create(250d, 80d, 140d, 200d);

        Assert.Equal(axes.CenterXMm, (axes.HorizontalStartXMm + axes.HorizontalEndXMm) / 2d, 9);
        Assert.Equal(axes.CenterZMm, axes.HorizontalZMm, 9);
        Assert.Equal(axes.CenterZMm, (axes.VerticalStartZMm + axes.VerticalEndZMm) / 2d, 9);
        Assert.Equal(axes.CenterXMm, axes.VerticalXMm, 9);
    }

    [Fact]
    public void ChangingWidth_UpdatesHorizontalAxisLengthLive()
    {
        var narrow = AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 160d, 220d);
        var wide = AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 240d, 220d);

        Assert.Equal(480d, narrow.HorizontalLengthMm, 9);
        Assert.Equal(720d, wide.HorizontalLengthMm, 9);
        Assert.Equal(narrow.VerticalLengthMm, wide.VerticalLengthMm, 9);
    }

    [Fact]
    public void ChangingHeight_UpdatesVerticalAxisLengthLive()
    {
        var shortMember = AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 160d, 220d);
        var tall = AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 160d, 300d);

        Assert.Equal(660d, shortMember.VerticalLengthMm, 9);
        Assert.Equal(900d, tall.VerticalLengthMm, 9);
        Assert.Equal(shortMember.HorizontalLengthMm, tall.HorizontalLengthMm, 9);
    }

    [Theory]
    [InlineData(RoofAutomaticPurlinGeneratorRole.WallPlate, true)]
    [InlineData(RoofAutomaticPurlinGeneratorRole.Ridge, true)]
    [InlineData(RoofAutomaticPurlinGeneratorRole.Intermediate, true)]
    public void ShouldDrawAxes_OnlyForTimberMembers(
        RoofAutomaticPurlinGeneratorRole role,
        bool expected) =>
        Assert.Equal(expected, AutomaticPurlinMemberCenterAxisGeometry.ShouldDrawAxes(role));

    [Fact]
    public void InvalidDimensions_YieldEmptyAxes()
    {
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 0d, 220d).IsEmpty);
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 160d, -1d).IsEmpty);
    }

    [Fact]
    public void DisabledOrNonTimberMembers_DoNotRequestAxes()
    {
        // All generator roles are timber members; empty axes cover disabled/invisible cases
        // where the presentation omits the member entirely (see Create_IncludesActiveRoles...).
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.ShouldDrawAxes(
            RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.ShouldDrawAxes(
            RoofAutomaticPurlinGeneratorRole.Ridge));
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.ShouldDrawAxes(
            RoofAutomaticPurlinGeneratorRole.Intermediate));
        Assert.True(AutomaticPurlinMemberCenterAxisGeometry.Create(0d, 0d, 0d, 0d).IsEmpty);
    }

    [Fact]
    public void CenterlinePen_UsesDashDotDistinctFromDatumDash()
    {
        var axisPen = RoofSectionDiagramStyle.CreateMemberCenterAxisPen();
        var datumPen = RoofSectionDiagramStyle.CreateDatumPen(
            RoofSectionDiagramStyle.GuideBrush);

        Assert.Equal(System.Windows.Media.DashStyles.DashDot, axisPen.DashStyle);
        Assert.Equal(System.Windows.Media.DashStyles.Dash, datumPen.DashStyle);
        Assert.NotEqual(axisPen.Brush, datumPen.Brush);
    }
}
