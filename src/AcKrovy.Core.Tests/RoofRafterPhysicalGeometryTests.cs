using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterPhysicalGeometryTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(30d)]
    [InlineData(45d)]
    public void FaceNormal_IsUnitUpwardAndMatchesPitch(double pitchDegrees)
    {
        var normal = NormalForPitch(pitchDegrees);

        Assert.Equal(1d, normal.Length, 10);
        Assert.True(normal.Z > 0d);
        Assert.Equal(Math.Cos(Radians(pitchDegrees)), normal.Z, 10);
    }

    [Theory]
    [InlineData(17d)]
    [InlineData(90d)]
    [InlineData(233d)]
    public void FaceNormal_IsInvariantUnderXyRotation(double rotationDegrees)
    {
        var pitch = 30d;
        var basePoints = FacePoints(pitch);
        var rotated = basePoints.Select(point => Rotate(point, rotationDegrees)).ToArray();

        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            rotated[0], rotated[1], rotated[2], out var actual));
        var expected = Rotate(NormalForPitch(pitch), rotationDegrees);

        Assert.Equal(expected.X, actual.X, 10);
        Assert.Equal(expected.Y, actual.Y, 10);
        Assert.Equal(expected.Z, actual.Z, 10);
    }

    [Fact]
    public void RafterSection_UsesRoofNormalForUpperAndLowerSurfaces()
    {
        var normal = NormalForPitch(30d);
        var center = new RoofPoint3D(10d, 20d, 1000d);

        Assert.True(RoofRafterPhysicalGeometry.TryCreateRafterSection(
            center, normal, 160d, out var section));

        Assert.Equal(center.X - normal.X * 80d, section!.LowerSurfacePoint.X, 10);
        Assert.Equal(center.Y - normal.Y * 80d, section.LowerSurfacePoint.Y, 10);
        Assert.Equal(1000d - Math.Cos(Math.PI / 6d) * 80d,
            section.LowerSurfacePoint.Z, 10);
        Assert.Equal(1000d + Math.Cos(Math.PI / 6d) * 80d,
            section.UpperSurfacePoint.Z, 10);
    }

    [Fact]
    public void PercentSeating_NumericalThirtyDegreeFixtureIsExact()
    {
        var depth = 160d * 25d / 100d;
        var result = Placement(1000d, 30d, 160d, 220d, depth);
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);

        Assert.Equal(40d, placement.SeatingDepthMm, 10);
        Assert.Equal(930.7179676972449d, placement.RafterLowerSurfaceLocalZMm, 9);
        Assert.Equal(1069.282032302755d, placement.RafterUpperSurfaceLocalZMm, 9);
        Assert.Equal(965.3589838486224d, placement.PurlinTopLocalZMm, 9);
        Assert.Equal(855.3589838486224d, placement.PurlinCenterLocalZMm, 9);
        Assert.Equal(745.3589838486224d, placement.PurlinBottomLocalZMm, 9);
    }

    [Fact]
    public void AbsoluteSeating_UsesSameNormalProjectedFormula()
    {
        var result = Placement(1000d, 45d, 180d, 240d, 35d);
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);
        var expectedTop = 1000d + Math.Cos(Math.PI / 4d) * (35d - 90d);

        Assert.Equal(35d, placement.SeatingDepthMm, 10);
        Assert.Equal(expectedTop, placement.PurlinTopLocalZMm, 10);
        Assert.Equal(expectedTop - 120d, placement.PurlinCenterLocalZMm, 10);
        Assert.Equal(expectedTop - 240d, placement.PurlinBottomLocalZMm, 10);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(160d)]
    [InlineData(161d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSeating_FailsClosed(double seatingDepthMm)
    {
        var result = Placement(1000d, 30d, 160d, 220d, seatingDepthMm);

        Assert.False(result.IsValid);
        Assert.Null(result.Placement);
        Assert.Equal(RoofPurlinPhysicalPlacementError.InvalidSeatingDepth, result.Error);
    }

    [Fact]
    public void InvalidNormalAndSectionDimensionsFailClosed()
    {
        var datum = Datum(0d);
        var center = new RoofPoint3D(0d, 0d, 1000d);

        Assert.Equal(RoofPurlinPhysicalPlacementError.InvalidFaceNormal,
            RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                center, default, 160d, 220d, 40d, datum).Error);
        Assert.Equal(RoofPurlinPhysicalPlacementError.InvalidRafterHeight,
            RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                center, NormalForPitch(30d), 0d, 220d, 40d, datum).Error);
        Assert.Equal(RoofPurlinPhysicalPlacementError.InvalidPurlinHeight,
            RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                center, NormalForPitch(30d), 160d, double.NaN, 40d, datum).Error);
    }

    [Fact]
    public void WcsTranslationShiftsLocalValuesAndPreservesRelativeElevations()
    {
        var baseline = Placement(1000d, 30d, 160d, 220d, 40d, datumLocalZ: 0d);
        var translated = Placement(1500d, 30d, 160d, 220d, 40d, datumLocalZ: 500d);
        var first = Assert.IsType<RoofPurlinPhysicalPlacement>(baseline.Placement);
        var second = Assert.IsType<RoofPurlinPhysicalPlacement>(translated.Placement);

        Assert.Equal(first.PurlinCenterLocalZMm + 500d, second.PurlinCenterLocalZMm, 10);
        Assert.Equal(first.RafterLowerSurfaceLocalZMm + 500d,
            second.RafterLowerSurfaceLocalZMm, 10);
        Assert.Equal(first.PurlinCenterRelativeElevationMm,
            second.PurlinCenterRelativeElevationMm, 10);
        Assert.Equal(first.RafterCenterRelativeElevationMm,
            second.RafterCenterRelativeElevationMm, 10);
    }

    private static RoofPurlinPhysicalPlacementResult Placement(
        double centerZ,
        double pitchDegrees,
        double rafterHeightMm,
        double purlinHeightMm,
        double seatingDepthMm,
        double datumLocalZ = 0d) => RoofRafterPhysicalGeometry.CreatePurlinPlacement(
            new RoofPoint3D(0d, 0d, centerZ),
            NormalForPitch(pitchDegrees),
            rafterHeightMm,
            purlinHeightMm,
            seatingDepthMm,
            Datum(datumLocalZ));

    private static RoofRelativeElevationDatum Datum(double localZ) => new(
        RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
        3800d,
        localZ);

    private static RoofFaceUnitNormal NormalForPitch(double pitchDegrees)
    {
        var points = FacePoints(pitchDegrees);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            points[0], points[1], points[2], out var normal));
        return normal;
    }

    private static RoofPoint3D[] FacePoints(double pitchDegrees) =>
    [
        new RoofPoint3D(0d, 0d, 0d),
        new RoofPoint3D(1000d, 0d, 0d),
        new RoofPoint3D(0d, 1000d, 1000d * Math.Tan(Radians(pitchDegrees))),
    ];

    private static RoofPoint3D Rotate(RoofPoint3D point, double degrees)
    {
        var radians = Radians(degrees);
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new RoofPoint3D(
            point.X * cosine - point.Y * sine,
            point.X * sine + point.Y * cosine,
            point.Z);
    }

    private static RoofFaceUnitNormal Rotate(RoofFaceUnitNormal normal, double degrees)
    {
        var point = Rotate(new RoofPoint3D(normal.X, normal.Y, normal.Z), degrees);
        return new RoofFaceUnitNormal(point.X, point.Y, point.Z);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}
