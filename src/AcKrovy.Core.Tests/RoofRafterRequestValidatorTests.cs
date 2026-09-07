using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterRequestValidatorTests
{
    [Fact]
    public void FirstUseDefaultsAreCanonicalAndDoNotContainSlope()
    {
        var preferences = RoofRafterPreferences.CreateFirstUse("Smrek C24");

        Assert.Equal(80d, preferences.WidthMm);
        Assert.Equal(160d, preferences.HeightMm);
        Assert.Equal(900d, preferences.MaximumSpacingMm);
        Assert.Equal("Smrek C24", preferences.Material);
        Assert.DoesNotContain("Slope", typeof(RoofRafterPreferences).GetProperties().Select(item => item.Name));
    }

    [Fact]
    public void ValidRequestUsesRoofSlopeAndAllUserInputs()
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 44),
            100,
            180,
            1000,
            500,
            "KVH C24 NSi");

        Assert.True(result.IsValid);
        Assert.Equal(100d, result.Request!.WidthMm);
        Assert.Equal(180d, result.Request.HeightMm);
        Assert.Equal(1000d, result.Request.MaximumSpacingMm);
        Assert.Equal("KVH C24 NSi", result.Request.Material);
        Assert.Equal(44d, result.Request.RoofSlopeDegrees);
        Assert.Equal(50d / 10000d, result.Layout!.Rafters[0].StationFraction, 12);
        Assert.Equal(990d, result.Layout.ActualSpacingMm, 9);
    }

    [Fact]
    public void PreferencesRoundTripExcludesRoofSlope()
    {
        var request = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 31), 120, 200, 850, 500, "Smrek C16").Request!;

        Assert.Equal(new RoofRafterPreferences(120, 200, 850, "Smrek C16"), request.ToPreferences());
        Assert.Equal(31d, request.RoofSlopeDegrees);
    }

    [Fact]
    public void MonopitchRequestUsesOneNeutralPlaneAndPrimarySlope()
    {
        var geometry = MonopitchGeometry(10000, 6000, 27);

        var result = RoofRafterRequestValidator.Validate(
            geometry,
            100,
            180,
            1000,
            500,
            "KVH C24 NSi");

        Assert.True(result.IsValid);
        Assert.Equal(27d, result.Request!.RoofSlopeDegrees);
        Assert.Single(result.Layout!.Planes);
        Assert.Equal(result.Layout.StationCount, result.Layout.Rafters.Count);
        Assert.All(result.Layout.Rafters, rafter =>
        {
            Assert.Equal(RafterRoofFace.Face0, rafter.Face);
            Assert.Equal(geometry.LowToHighDirection, rafter.RunDirection);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GableRequestsStillUseTwoNeutralPlanes(bool asymmetric)
    {
        var geometry = asymmetric
            ? AsymmetricGeometry(10000, 8000, 20, 35)
            : Geometry(10000, 8000, 30);

        var result = RoofRafterRequestValidator.Validate(
            geometry, 80, 160, 900, 500, "Smrek C24");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Layout!.Planes.Count);
        Assert.Equal(result.Layout.StationCount * 2, result.Layout.Rafters.Count);
    }

    [Theory]
    [InlineData(0d, RoofRafterRequestValidationError.InvalidWidth)]
    [InlineData(-1d, RoofRafterRequestValidationError.InvalidWidth)]
    [InlineData(double.NaN, RoofRafterRequestValidationError.InvalidWidth)]
    [InlineData(10000d, RoofRafterRequestValidationError.WidthDoesNotFitRoof)]
    [InlineData(10001d, RoofRafterRequestValidationError.WidthDoesNotFitRoof)]
    public void InvalidWidthIsRejected(double width, RoofRafterRequestValidationError error)
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 30), width, 160, 900, 500, "Smrek C24");

        Assert.False(result.IsValid);
        Assert.Equal(error, result.Error);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidHeightIsRejected(double height)
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 30), 80, height, 900, 500, "Smrek C24");

        Assert.Equal(RoofRafterRequestValidationError.InvalidHeight, result.Error);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double spacing)
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 30), 80, 160, spacing, 500, "Smrek C24");

        Assert.Equal(RoofRafterRequestValidationError.InvalidMaximumSpacing, result.Error);
    }

    [Theory]
    [InlineData(499d, 500d, false)]
    [InlineData(500d, 500d, true)]
    [InlineData(449d, 450d, false)]
    [InlineData(450d, 450d, true)]
    [InlineData(600d, 650d, false)]
    [InlineData(650d, 650d, true)]
    public void AutomaticRequestUsesConfiguredMinimum(
        double workingSpacing,
        double minimumSpacing,
        bool expected)
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 30),
            80,
            160,
            workingSpacing,
            minimumSpacing,
            "Smrek C24");

        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMaterialIsRejected(string? material)
    {
        var result = RoofRafterRequestValidator.Validate(
            Geometry(10000, 8000, 30), 80, 160, 900, 500, material);

        Assert.Equal(RoofRafterRequestValidationError.InvalidMaterial, result.Error);
    }

    private static SimpleGableRoofGeometry Geometry(double length, double width, double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(length, 0), new(length, width), new(0, width)],
            true));
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid);
        return result.Geometry!;
    }

    private static SimpleGableRoofGeometry AsymmetricGeometry(
        double length,
        double width,
        double face0Slope,
        double face1Slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(length, 0), new(length, width), new(0, width)],
            true));
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(face0Slope, direction, Face1SlopeDegrees: face1Slope),
            RoofKind.AsymmetricGable));
        Assert.True(result.IsValid);
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static MonopitchRoofGeometry MonopitchGeometry(
        double length,
        double width,
        double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(length, 0), new(length, width), new(0, width)],
            true));
        Assert.True(RoofDirection2D.TryCreate(0, 1, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, SlopeDirection: direction),
            RoofKind.Monopitch));
        Assert.True(result.IsValid);
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }
}
