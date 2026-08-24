using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterMaterializationRulesTests
{
    [Theory]
    [InlineData(0d, false, 10000d, 6000d)]
    [InlineData(30d, false, 10000d, 6000d)]
    [InlineData(30d, true, 10000d, 6000d)]
    [InlineData(0d, false, 14000d, 8000d)]
    public void MonopitchLayout_IsOnePlaneMaterializableWithUniqueStableKeys(
        double rotationDegrees,
        bool mirrored,
        double lengthMm,
        double widthMm)
    {
        var geometry = SolveMonopitch(rotationDegrees, mirrored, lengthMm, widthMm);
        var layout = SolveLayout(geometry);

        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        var plane = Assert.Single(layout.Planes);
        Assert.Equal(RafterRoofFace.Face0, plane.Face);
        Assert.Equal(layout.StationCount, layout.Rafters.Count);
        Assert.Equal(
            layout.Rafters.Count,
            layout.Rafters.Select(item => item.LogicalKey).Distinct().Count());
        Assert.DoesNotContain(layout.Rafters, item => item.Face == RafterRoofFace.Face1);
        Assert.All(layout.Rafters, rafter =>
        {
            var timber = TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                SlopeDegrees = rafter.SlopeDegrees,
            };
            Assert.Equal(
                rafter.TrueLengthMm,
                TimberCalculator.CalculateActualLengthMm(timber, rafter.PlanLengthMm),
                8);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GableLayouts_RemainMaterializableWithoutChangingTwoFaceConvention(
        bool asymmetric)
    {
        var geometry = SolveGable(asymmetric);
        var layout = SolveLayout(geometry);

        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        Assert.Equal(new[] { RafterRoofFace.Face0, RafterRoofFace.Face1 },
            layout.Planes.Select(item => item.Face));
        Assert.Equal(layout.StationCount * 2, layout.Rafters.Count);
    }

    [Fact]
    public void InconsistentLayout_IsRejectedBeforeCadMaterialization()
    {
        var geometry = SolveMonopitch(0d, false, 10000d, 6000d);
        var layout = SolveLayout(geometry);

        Assert.False(RoofRafterMaterializationRules.IsConsistent(
            geometry,
            layout with { Rafters = layout.Rafters.Skip(1).ToArray() }));
        Assert.False(RoofRafterMaterializationRules.IsConsistent(
            geometry,
            layout with { Signature = "stale-layout" }));
    }

    private static RoofRafterLayout SolveLayout(IRoofGeometry geometry)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(1000d, 100d));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry SolveMonopitch(
        double rotationDegrees,
        bool mirrored,
        double lengthMm,
        double widthMm)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        var x = Direction(Math.Cos(radians), Math.Sin(radians));
        var y = Direction(-Math.Sin(radians), Math.Cos(radians));
        RoofPoint2D Point(double along, double across) =>
            new(350d + along * x.X + across * y.X, -725d + along * x.Y + across * y.Y);
        var footprint = Validate([
            Point(0d, 0d),
            Point(lengthMm, 0d),
            Point(lengthMm, widthMm),
            Point(0d, widthMm),
        ]);
        var definition = new RoofDefinition(
            footprint,
            new RoofParameters(30d, SlopeDirection: y),
            RoofKind.Monopitch);
        if (mirrored)
        {
            definition = MonopitchRoofDefinitionRules.Mirror(definition);
        }
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static SimpleGableRoofGeometry SolveGable(bool asymmetric)
    {
        var footprint = Validate([
            new(0d, 0d), new(10000d, 0d), new(10000d, 6000d), new(0d, 6000d),
        ]);
        var definition = new RoofDefinition(
            footprint,
            asymmetric
                ? new RoofParameters(20d, Direction(1d, 0d), Face1SlopeDegrees: 35d)
                : new RoofParameters(30d, Direction(1d, 0d)),
            asymmetric ? RoofKind.AsymmetricGable : RoofKind.SimpleGable);
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }
}
