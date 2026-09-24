using System.Windows;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinSectionSvgTemplateTests
{
    [Fact]
    public void MasterSceneRetainsSuppliedViewBoxAndSemanticPaths()
    {
        Assert.Equal(new Rect(0d, 0d, 11562.1457d, 4179.493d),
            AutomaticPurlinSectionSvgTemplate.MasterViewBox);
        Assert.Equal(
        [
            "rafter-left", "rafter-right", "wall-left", "wall-right",
            "wallplate-left", "wallplate-right", "purlin-left-1",
            "purlin-right-1", "ridge-purlin",
        ], AutomaticPurlinSectionSvgTemplate.ElementIds);
        Assert.True(AutomaticPurlinSectionSvgTemplate.IsRafterLeftElement("rafter-left_x002c_"));
    }

    [Theory]
    [InlineData(RoofAutomaticPurlinGeneratorRole.WallPlate, -1, "wallplate-left")]
    [InlineData(RoofAutomaticPurlinGeneratorRole.WallPlate, 1, "wallplate-right")]
    [InlineData(RoofAutomaticPurlinGeneratorRole.Intermediate, -1, "purlin-left-1")]
    [InlineData(RoofAutomaticPurlinGeneratorRole.Intermediate, 1, "purlin-right-1")]
    [InlineData(RoofAutomaticPurlinGeneratorRole.Ridge, 0, "ridge-purlin")]
    public void MemberRolesMapToMasterSemanticGroups(
        RoofAutomaticPurlinGeneratorRole role,
        int side,
        string expectedElementId)
    {
        Assert.Equal(expectedElementId,
            AutomaticPurlinSectionSvgTemplate.GetMemberTemplateElementId(
                CreateMember(role, (AutomaticPurlinSectionSide)side)));
    }

    [Fact]
    public void WallPlateRetainsItsWallRelativeMasterPosition()
    {
        var plate = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("wallplate-left");
        var wall = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("wall-left");

        Assert.Equal(wall.Top, plate.Bottom, 3);
        Assert.True(wall.Left < plate.Left);
        Assert.True(wall.Right > plate.Right);
    }

    [Fact]
    public void PurlinPairRetainsMatchingMasterLocalProportions()
    {
        var left = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("purlin-left-1");
        var right = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("purlin-right-1");

        Assert.Equal(left.Width / left.Height, right.Width / right.Height, 5);
        Assert.Equal(left.Top, right.Top, 8);
    }

    [Fact]
    public void ViewportTransformPreservesMasterSceneAspectRatio()
    {
        var transform = AutomaticPurlinSectionSvgTemplate.CreateAspectPreservingViewportTransform(
            new Size(900d, 500d), 24d);

        Assert.Equal(transform.M11, transform.M22, 12);
        Assert.Equal(0d, transform.M12, 12);
        Assert.Equal(0d, transform.M21, 12);
    }

    [Fact]
    public void DynamicSceneClonesSemanticPairWithoutChangingMasterGeometry()
    {
        var presentation = CreatePresentation();
        var before = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("purlin-left-1");
        var drawing = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => new Point(x, -z));
        var after = AutomaticPurlinSectionSvgTemplate.GetMasterElementBounds("purlin-left-1");

        Assert.Equal(8, drawing.Children.Count);
        Assert.Equal(before, after);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation()
    {
        var rafters = new[]
        {
            new AutomaticPurlinSectionRafterMm(
                AutomaticPurlinSectionSide.Left,
                new AutomaticPurlinSectionLineMm(-3000d, 0d, 0d, 1800d),
                [new(-3000d, 80d), new(0d, 1880d), new(0d, 1720d), new(-3000d, -80d)]),
            new AutomaticPurlinSectionRafterMm(
                AutomaticPurlinSectionSide.Right,
                new AutomaticPurlinSectionLineMm(0d, 1800d, 3000d, 0d),
                [new(0d, 1880d), new(3000d, 80d), new(3000d, -80d), new(0d, 1720d)]),
        };
        var members = new[]
        {
            CreateMember(RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinSectionSide.Left) with { CenterXMm = -2800d, CenterZMm = 50d },
            CreateMember(RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinSectionSide.Right) with { CenterXMm = 2800d, CenterZMm = 50d },
            CreateMember(RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinSectionSide.Left) with { CenterXMm = -1500d, CenterZMm = 900d },
            CreateMember(RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinSectionSide.Right) with { CenterXMm = 1500d, CenterZMm = 900d },
        };
        var walls = new[]
        {
            new AutomaticPurlinSectionWallMm(AutomaticPurlinSectionSide.Left, -2800d, -20d, -700d, 260d),
            new AutomaticPurlinSectionWallMm(AutomaticPurlinSectionSide.Right, 2800d, -20d, -700d, 260d),
        };
        return new AutomaticPurlinSectionPresentation(
            [], rafters, walls, members, -3080d, 3080d, -700d, 1880d, 160d);
    }

    private static AutomaticPurlinSectionMemberMm CreateMember(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinSectionSide side) =>
        new(
            $"{role}-{side}", role, "title", "160 × 180", "bottom", "axis", "top",
            side, 0d, 100d, 160d, 180d, null, null);
}
