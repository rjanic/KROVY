using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAbsoluteElevationRulesTests
{
    [Theory]
    [InlineData(3000d, 15d)]
    [InlineData(3000d, 45d)]
    [InlineData(3000d, 55d)]
    public void RiseMm_MatchesHalfWidthTimesTanPitch(double halfWidth, double pitch)
    {
        var expected = halfWidth * Math.Tan(pitch * Math.PI / 180d);
        Assert.Equal(expected, RoofAbsoluteElevationRules.RiseMm(halfWidth, pitch), 12);
        // Spec reference magnitudes for width=6000 (half=3000).
        if (pitch == 15d)
        {
            Assert.Equal(803.847577293368d, expected, 6);
        }
        else if (pitch == 45d)
        {
            Assert.Equal(3000d, expected, 9);
        }
        else if (pitch == 55d)
        {
            Assert.Equal(4284.444d, expected, 3);
        }
    }

    [Fact]
    public void EaveMode_PreservesEaveAndComputesRidge()
    {
        var state = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            halfRoofWidthMm: 3000d,
            pitchDegrees: 45d,
            physical3DEnabled: true);
        Assert.Equal(3000d, state.ResolvedEaveRelativeElevationMm);
        Assert.Equal(6000d, state.ResolvedRidgeRelativeElevationMm);
        Assert.Equal(3000d, state.EnteredRelativeElevationMm);
        Assert.True(state.Physical3DEnabled);
    }

    [Fact]
    public void RidgeMode_PreservesRidgeAndComputesEave()
    {
        var state = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Ridge,
            6000d,
            halfRoofWidthMm: 3000d,
            pitchDegrees: 45d,
            physical3DEnabled: false);
        Assert.Equal(3000d, state.ResolvedEaveRelativeElevationMm, 9);
        Assert.Equal(6000d, state.ResolvedRidgeRelativeElevationMm, 9);
    }

    [Fact]
    public void SwitchMode_DoesNotMovePhysicalGeometry()
    {
        var eave = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            45d,
            true);
        var ridge = RoofAbsoluteElevationRules.SwitchMode(
            eave,
            RoofAbsoluteElevationInputMode.Ridge);
        Assert.Equal(RoofAbsoluteElevationInputMode.Ridge, ridge.InputMode);
        Assert.Equal(6000d, ridge.EnteredRelativeElevationMm);
        Assert.Equal(eave.ResolvedEaveRelativeElevationMm, ridge.ResolvedEaveRelativeElevationMm);
        Assert.Equal(eave.ResolvedRidgeRelativeElevationMm, ridge.ResolvedRidgeRelativeElevationMm);

        var back = RoofAbsoluteElevationRules.SwitchMode(
            ridge,
            RoofAbsoluteElevationInputMode.Eave);
        Assert.Equal(eave.ResolvedEaveRelativeElevationMm, back.ResolvedEaveRelativeElevationMm);
        Assert.Equal(eave.ResolvedRidgeRelativeElevationMm, back.ResolvedRidgeRelativeElevationMm);
    }

    [Fact]
    public void RepeatedModeSwitchAndPitchChange_HasNoCumulativeDrift()
    {
        var state = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            45d,
            true);
        for (var i = 0; i < 50; i++)
        {
            state = RoofAbsoluteElevationRules.SwitchMode(
                state,
                i % 2 == 0
                    ? RoofAbsoluteElevationInputMode.Ridge
                    : RoofAbsoluteElevationInputMode.Eave);
            state = RoofAbsoluteElevationRules.RecalculateForGeometry(
                state,
                halfRoofWidthMm: 3000d,
                pitchDegrees: i % 2 == 0 ? 30d : 45d);
            state = RoofAbsoluteElevationRules.RecalculateForGeometry(
                state,
                halfRoofWidthMm: 3000d,
                pitchDegrees: 45d);
        }

        Assert.Equal(3000d, state.ResolvedEaveRelativeElevationMm, 12);
        Assert.Equal(6000d, state.ResolvedRidgeRelativeElevationMm, 12);
    }

    [Fact]
    public void FormatMetres_ExactZeroUsesPlusMinus()
    {
        var sk = CultureInfo.GetCultureInfo("sk-SK");
        Assert.Equal("±0,000", RoofAbsoluteElevationRules.FormatMetres(0d, sk));
        Assert.Equal("+3,000", RoofAbsoluteElevationRules.FormatMetres(3000d, sk));
        Assert.Equal("-1,250", RoofAbsoluteElevationRules.FormatMetres(-1250d, sk));
    }

    [Fact]
    public void TryParseMetres_CultureAware()
    {
        Assert.True(RoofAbsoluteElevationRules.TryParseMetres(
            "+3,000",
            CultureInfo.GetCultureInfo("sk-SK"),
            out var mm));
        Assert.Equal(3000d, mm);
        Assert.True(RoofAbsoluteElevationRules.TryParseMetres(
            "±0,000",
            CultureInfo.GetCultureInfo("sk-SK"),
            out var zero));
        Assert.Equal(0d, zero);
    }
}
