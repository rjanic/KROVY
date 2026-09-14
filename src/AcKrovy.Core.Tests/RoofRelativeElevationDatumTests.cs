using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRelativeElevationDatumTests
{
    [Fact]
    public void RelativeElevation_UsesExplicitLocalDatumOffset()
    {
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            3580d,
            125d);

        Assert.Equal(3580d, RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, 125d));
        Assert.Equal(4080d, RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, 625d));
        Assert.Equal(625d, RoofRelativeElevationDatumRules.ToLocalZMm(datum, 4080d));
    }

    [Theory]
    [InlineData(0d, "±0.000")]
    [InlineData(3580d, "+3.580")]
    [InlineData(4300d, "+4.300")]
    [InlineData(-250d, "-0.250")]
    public void Formatter_UsesSignedMetresWithExactlyThreeDecimals(
        double millimetres,
        string expected) =>
        Assert.Equal(expected, RoofRelativeElevationDatumRules.FormatMetres(millimetres));

    [Theory]
    [InlineData("+3.580", "sk-SK", 3580d)]
    [InlineData("+3,580", "sk-SK", 3580d)]
    [InlineData("±0.000", "en-US", 0d)]
    [InlineData("-0.250", "en-US", -250d)]
    public void EditableMetres_ParsesCultureAndCanonicalBuildingNotation(
        string text,
        string cultureName,
        double expectedMm)
    {
        Assert.True(RoofRelativeElevationDatumRules.TryParseMetres(
            text,
            CultureInfo.GetCultureInfo(cultureName),
            out var actualMm));
        Assert.Equal(expectedMm, actualMm, 9);
    }

    [Fact]
    public void ArchitecturalRelativeElevation_IsInvariantUnderWcsZTranslation()
    {
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            3580d,
            0d);
        var localBottomCenterTop = new[] { 500d, 610d, 720d };
        var firstWcs = localBottomCenterTop.Select(localZ => 1000d + localZ).ToArray();
        var translatedWcs = localBottomCenterTop.Select(localZ => 9000d + localZ).ToArray();
        var firstRelative = firstWcs.Select(wcsZ =>
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, wcsZ - 1000d)).ToArray();
        var translatedRelative = translatedWcs.Select(wcsZ =>
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, wcsZ - 9000d)).ToArray();

        Assert.False(firstWcs.SequenceEqual(translatedWcs));
        Assert.Equal(firstRelative, translatedRelative);
        Assert.Equal(new[] { 4080d, 4190d, 4300d }, firstRelative);
    }

    [Theory]
    [InlineData(double.NaN, 0d, RoofRelativeElevationDatumError.InvalidReferenceRelativeElevation)]
    [InlineData(0d, double.PositiveInfinity, RoofRelativeElevationDatumError.InvalidReferenceLocalZ)]
    public void NonfiniteDatumFailsClosed(
        double referenceRelative,
        double referenceLocal,
        RoofRelativeElevationDatumError expected)
    {
        var result = RoofRelativeElevationDatumRules.Validate(
            1,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            referenceRelative,
            referenceLocal);

        Assert.False(result.IsValid);
        Assert.Null(result.Datum);
        Assert.Equal(expected, result.Error);
    }
}
