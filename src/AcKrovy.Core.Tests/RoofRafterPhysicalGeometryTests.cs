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
    public void RafterSection_UsesFixedHorizontalStationVerticalSpan()
    {
        var normal = NormalForPitch(30d);
        var upper = new RoofPoint3D(10d, 20d, 1000d);
        var verticalHalf = 160d / (2d * Math.Cos(Math.PI / 6d));

        Assert.True(RoofRafterPhysicalGeometry.TryCreateRafterSection(
            upper, normal, 160d, out var section));

        Assert.Equal(upper.X, section!.LowerSurfacePoint.X, 10);
        Assert.Equal(upper.Y, section.LowerSurfacePoint.Y, 10);
        Assert.Equal(1000d, section.UpperSurfacePoint.Z, 10);
        Assert.Equal(1000d - verticalHalf, section.CenterlinePoint.Z, 10);
        Assert.Equal(1000d - 2d * verticalHalf, section.LowerSurfacePoint.Z, 10);
    }

    [Fact]
    public void PercentSeating_FortyFiveDegreeHostOuterCornerFixture()
    {
        // Independent CAD: upper face at outer station 630, H=125, D=31.25, pitch 45°.
        // Top = 630 - (125-31.25)/cos45 = 497.417479…
        var depth = 125d * 25d / 100d;
        var upperAtAxis = new RoofPoint3D(0d, 0d, 700d);
        var result = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
            upperAtAxis,
            NormalForPitch(45d),
            125d,
            140d,
            depth,
            Datum(0d),
            purlinWidthMm: 140d);
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);

        Assert.Equal(31.25d, placement.SeatingDepthMm, 10);
        Assert.Equal(497.4174785275226d, placement.PurlinTopLocalZMm, 6);
        Assert.Equal(427.4174785275226d, placement.PurlinCenterLocalZMm, 6);
        Assert.Equal(357.4174785275226d, placement.PurlinBottomLocalZMm, 6);
        Assert.Equal(700d, placement.RafterUpperSurfaceLocalZMm, 9);
    }

    [Fact]
    public void InverseUpperFaceFromSeatedBottom_MatchesCreatePurlinPlacementHostFixture()
    {
        // Independent CAD: Bottom=0, H_wp=140, W=140, H=125, D=31.25, pitch 45°
        // → axisUpper = 140 + 93.75/cos45 + 70 = 342.582521…
        Assert.True(RoofRafterPhysicalGeometry.TryResolveUpperFaceLocalZFromSeatedBottom(
            bottomLocalZMm: 0d,
            purlinHeightMm: 140d,
            purlinWidthMm: 140d,
            rafterHeightMm: 125d,
            seatingDepthMm: 31.25d,
            pitchDegrees: 45d,
            out var upperFace));
        Assert.Equal(342.5825214724774d, upperFace, 6);

        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(
            RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                new RoofPoint3D(0d, 0d, upperFace),
                NormalForPitch(45d),
                125d,
                140d,
                31.25d,
                Datum(0d),
                purlinWidthMm: 140d).Placement);
        Assert.Equal(0d, placement.PurlinBottomLocalZMm, 6);
        Assert.Equal(70d, placement.PurlinCenterLocalZMm, 6);
        Assert.Equal(140d, placement.PurlinTopLocalZMm, 6);
        Assert.Equal(upperFace, placement.RafterUpperSurfaceLocalZMm, 6);
    }

    [Fact]
    public void AbsoluteSeating_UsesFixedHorizontalRemainingCover()
    {
        var result = Placement(1000d, 45d, 180d, 240d, 35d);
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);
        var expectedTop = 1000d - (180d - 35d) / Math.Cos(Math.PI / 4d);

        Assert.Equal(35d, placement.SeatingDepthMm, 10);
        Assert.Equal(expectedTop, placement.PurlinTopLocalZMm, 10);
        Assert.Equal(expectedTop - 120d, placement.PurlinCenterLocalZMm, 10);
        Assert.Equal(expectedTop - 240d, placement.PurlinBottomLocalZMm, 10);
    }

    [Theory]
    [InlineData(-1d)]
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
    public void FullHeightSeating_PlacesPurlinTopAtRafterUpperSurface()
    {
        var result = Placement(1000d, 30d, 160d, 220d, 160d);
        Assert.True(result.IsValid, result.Error.ToString());
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);
        Assert.Equal(160d, placement.SeatingDepthMm, 9);
        Assert.Equal(
            placement.RafterUpperSurfaceLocalZMm,
            placement.PurlinTopLocalZMm,
            9);
    }

    [Fact]
    public void ZeroSeating_PlacesPurlinTopAtRafterLowerSurface()
    {
        var result = Placement(1000d, 30d, 160d, 220d, 0d);
        Assert.True(result.IsValid, result.Error.ToString());
        var placement = Assert.IsType<RoofPurlinPhysicalPlacement>(result.Placement);
        Assert.Equal(0d, placement.SeatingDepthMm, 9);
        Assert.Equal(
            placement.RafterLowerSurfaceLocalZMm,
            placement.PurlinTopLocalZMm,
            9);
        Assert.True(placement.PurlinBottomLocalZMm < placement.PurlinTopLocalZMm);
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
        double upperFaceZ,
        double pitchDegrees,
        double rafterHeightMm,
        double purlinHeightMm,
        double seatingDepthMm,
        double datumLocalZ = 0d) => RoofRafterPhysicalGeometry.CreatePurlinPlacement(
            new RoofPoint3D(0d, 0d, upperFaceZ),
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
