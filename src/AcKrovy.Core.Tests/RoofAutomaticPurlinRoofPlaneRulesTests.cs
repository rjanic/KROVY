using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinRoofPlaneRulesTests
{
    /// <summary>
    /// HOST proof (AutoCAD physical measure): WallPlate 140×140, plan 700, H=125,
    /// pitch 45°, recess 25%, WallPlateBottom → RoofPlane 342.5825… mm → +0.343 m.
    /// </summary>
    [Fact]
    public void HostCase_WallPlate_125_45_140_25Percent_Is342_582521_DisplayPlus0_343()
    {
        const double rafterHeightMm = 125d;
        const double wallPlateMm = 140d;
        const double seatingPercent = 25d;
        const double pitchDegrees = 45d;

        var recessMm = rafterHeightMm * seatingPercent / 100d;
        Assert.Equal(31.25d, recessMm, 12);

        var remainingPerpendicularMm = rafterHeightMm - recessMm;
        Assert.Equal(93.75d, remainingPerpendicularMm, 12);

        var verticalCoverMm = remainingPerpendicularMm / Math.Cos(pitchDegrees * Math.PI / 180d);
        Assert.Equal(132.5825214724774d, verticalCoverMm, 9);

        var halfWidthRunMm = wallPlateMm / 2d;
        var axisRiseMm = halfWidthRunMm * Math.Tan(pitchDegrees * Math.PI / 180d);
        Assert.Equal(70d, axisRiseMm, 12);

        var expected =
            wallPlateMm + axisRiseMm + verticalCoverMm;
        Assert.Equal(342.5825214724774d, expected, 9);

        var actual = RoofAutomaticPurlinRoofPlaneRules.HostWallPlateRoofPlaneRelativeMm(
            rafterHeightMm,
            wallPlateMm,
            wallPlateMm,
            seatingPercent,
            pitchDegrees);
        Assert.Equal(expected, actual, 9);
        Assert.Equal("+0.343", RoofRelativeElevationDatumRules.FormatMetres(actual));
    }

    [Theory]
    [InlineData(0d, "+0.387")]
    [InlineData(25d, "+0.343")]
    [InlineData(50d, "+0.298")]
    [InlineData(75d, "+0.254")]
    [InlineData(100d, "+0.210")]
    public void HostWallPlate_SeatingSweep_RemainsConfirmed(double seatingPercent, string display)
    {
        var mm = RoofAutomaticPurlinRoofPlaneRules.HostWallPlateRoofPlaneRelativeMm(
            125d, 140d, 140d, seatingPercent, 45d);
        Assert.Equal(display, RoofRelativeElevationDatumRules.FormatMetres(mm));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void WallPlate_MemberFormula_MatchesClosedForm_At30And45(double seatingPercent)
    {
        foreach (var pitch in new[] { 30d, 45d })
        {
            const double h = 125d;
            const double w = 140d;
            const double topRel = 140d;
            var d = h * seatingPercent / 100d;
            var pitchRad = pitch * Math.PI / 180d;
            var expected = topRel + (w / 2d) * Math.Tan(pitchRad) + (h - d) / Math.Cos(pitchRad);

            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveMemberRoofPlaneRelativeMm(
                    topRel, w, h, d, pitch, out var actual));
            Assert.Equal(expected, actual, 9);
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void Intermediate_MemberFormula_MatchesClosedForm_At30And45(double seatingPercent)
    {
        foreach (var pitch in new[] { 30d, 45d })
        {
            const double h = 125d;
            const double w = 160d;
            const double topRel = 543d;
            var d = h * seatingPercent / 100d;
            var pitchRad = pitch * Math.PI / 180d;
            var expected = topRel + (w / 2d) * Math.Tan(pitchRad) + (h - d) / Math.Cos(pitchRad);

            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveMemberRoofPlaneRelativeMm(
                    topRel, w, h, d, pitch, out var actual));
            Assert.Equal(expected, actual, 9);
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void Ridge_UsesSameMemberFormula_NotCenterlineHalfOverCos(double seatingPercent)
    {
        foreach (var pitch in new[] { 30d, 45d })
        {
            const double h = 125d;
            const double w = 160d;
            const double topRel = 2430d;
            var d = h * seatingPercent / 100d;
            var pitchRad = pitch * Math.PI / 180d;
            var expected = topRel + (w / 2d) * Math.Tan(pitchRad) + (h - d) / Math.Cos(pitchRad);
            var wrongCenterline =
                2462.097d + h / (2d * Math.Cos(pitchRad));

            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveRidgeRoofPlaneRelativeMm(
                    topRel, w, h, d, pitch, out var actual));
            Assert.Equal(expected, actual, 9);
            Assert.NotEqual(wrongCenterline, actual, 0);
        }
    }

    [Fact]
    public void HostRidgeCase_Top2430_YieldsRoofPlane2642_58_DisplayPlus2_643()
    {
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveRidgeRoofPlaneRelativeMm(
                2430d, 160d, 125d, 31.25d, 45d, out var roofPlane));
        Assert.Equal(2642.5825214724774d, roofPlane, 9);
        Assert.Equal("+2.643", RoofRelativeElevationDatumRules.FormatMetres(roofPlane));
    }

    [Fact]
    public void MemberFormula_RejectsInvalidPitchAndOverDepth()
    {
        Assert.False(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveMemberRoofPlaneRelativeMm(
                140d, 140d, 125d, 31.25d, 90d, out _));
        Assert.False(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveMemberRoofPlaneRelativeMm(
                140d, 140d, 125d, 200d, 45d, out _));
        Assert.False(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveMemberRoofPlaneRelativeMm(
                140d, 0d, 125d, 31.25d, 45d, out _));
    }
}
