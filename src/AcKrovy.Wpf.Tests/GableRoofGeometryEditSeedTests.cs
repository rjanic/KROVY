using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// Edit-mode seeding of the shared GableRoofGeometryViewModel: an unchanged edit
/// must reproduce the exact persisted physical roof (kind, α, β, ΔH and the
/// PERSISTED ridge direction — never the footprint fallback), and both kind
/// conversions must enter a deterministic, valid state.
/// </summary>
public sealed class GableRoofGeometryEditSeedTests
{
    [Fact]
    public void SeedFromExistingSimpleGable_ReproducesTheExactPersistedRoof()
    {
        var footprint = Rectangle();
        var original = Solve(footprint, 30d, 30d, 1d, 0d, 0d, RoofKind.SimpleGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.SimpleGable);

        viewModel.SeedFromExistingGeometry(original);

        Assert.True(viewModel.IsSymmetricMode);
        Assert.False(viewModel.IsAsymmetricMode);
        Assert.Equal("30", viewModel.AlphaText);
        Assert.Equal("30", viewModel.BetaText);
        Assert.Equal("0", viewModel.EaveHeightDifferenceText);
        Assert.True(viewModel.HasRidgeDirection);
        Assert.True(viewModel.TryGetGeometry(out var reproduced));
        Assert.Equal(original.Signature, reproduced!.Signature);
        Assert.Equal(original.Ridge, reproduced.Ridge);
        Assert.Equal(viewModel.SectionState!.RunAMm, viewModel.SectionState.RunBMm, 8);
        Assert.Equal(0d, viewModel.SectionState.EaveAElevationMm);
        Assert.Equal(0d, viewModel.SectionState.EaveBElevationMm);
    }

    [Fact]
    public void SeedFromExistingAsymmetric_ReproducesSlopesDeltaHeightAndDirection()
    {
        var footprint = Rectangle();
        var original = Solve(footprint, 20d, 35d, 1d, 0d, 450d, RoofKind.AsymmetricGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.AsymmetricGable);

        viewModel.SeedFromExistingGeometry(original);

        Assert.True(viewModel.IsAsymmetricMode);
        Assert.True(viewModel.IsDeltaHeightMode);
        Assert.False(viewModel.IsAsymmetryMirrored);
        Assert.Equal("20", viewModel.AlphaText);
        Assert.Equal("35", viewModel.BetaText);
        Assert.Equal("450", viewModel.EaveHeightDifferenceText);
        Assert.True(viewModel.TryGetGeometry(out var reproduced));
        Assert.Equal(original.Signature, reproduced!.Signature);
        Assert.Equal(450d, reproduced.EaveHeightDifferenceMm);
        Assert.Equal(20d, reproduced.Face0SlopeDegrees);
        Assert.Equal(35d, reproduced.Face1SlopeDegrees);
        Assert.Equal(original.RidgeDirection.X, reproduced.RidgeDirection.X, 9);
        Assert.Equal(original.RidgeDirection.Y, reproduced.RidgeDirection.Y, 9);
    }

    [Fact]
    public void SeedUsesThePersistedRidgeDirectionNeverTheFootprintFallback()
    {
        // Footprint edge 0-1 runs along X, so the ViewModel fallback direction is
        // (1,0). A roof persisted with the OTHER rectangle edge (0,1) must seed the
        // persisted direction, otherwise an unchanged edit would flip the ridge.
        var footprint = Rectangle();
        var original = Solve(footprint, 25d, 25d, 0d, 1d, 0d, RoofKind.SimpleGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.SimpleGable);

        viewModel.SeedFromExistingGeometry(original);

        Assert.True(viewModel.HasRidgeDirection);
        Assert.True(viewModel.TryGetGeometry(out var reproduced));
        Assert.Equal(original.Signature, reproduced!.Signature);
        Assert.Equal(0d, reproduced.RidgeDirection.X, 9);
        Assert.Equal(1d, reproduced.RidgeDirection.Y, 9);
        Assert.Equal(original.Ridge, reproduced.Ridge);
    }

    [Fact]
    public void SeedSimpleThenSwitchToAsymmetric_EntersDeterministicSymmetricGeometry()
    {
        var footprint = Rectangle();
        var original = Solve(footprint, 30d, 30d, 1d, 0d, 0d, RoofKind.SimpleGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.SimpleGable);
        viewModel.SeedFromExistingGeometry(original);

        viewModel.SelectedKind = RoofKind.AsymmetricGable;

        Assert.True(viewModel.IsAsymmetricMode);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryGetGeometry(out var geometry));
        Assert.Equal(RoofKind.AsymmetricGable, geometry!.Kind);
        Assert.Equal(geometry.Face0SlopeDegrees, geometry.Face1SlopeDegrees, 9);
        Assert.Equal(0d, geometry.EaveHeightDifferenceMm);
        Assert.Equal(geometry.Face0RunMm, geometry.Face1RunMm, 8);
        Assert.Equal(0d, viewModel.SectionState!.EaveBElevationMm);
    }

    [Fact]
    public void SeedAsymmetricThenSwitchToSimple_ForcesEqualSlopesAndZeroDeltaHeight()
    {
        var footprint = Rectangle();
        var original = Solve(footprint, 20d, 35d, 1d, 0d, 450d, RoofKind.AsymmetricGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.AsymmetricGable);
        viewModel.SeedFromExistingGeometry(original);

        viewModel.SelectedKind = RoofKind.SimpleGable;

        Assert.True(viewModel.IsSymmetricMode);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryGetGeometry(out var geometry));
        Assert.Equal(RoofKind.SimpleGable, geometry!.Kind);
        Assert.Equal(geometry.Face0SlopeDegrees, geometry.Face1SlopeDegrees, 9);
        Assert.Equal(0d, geometry.EaveHeightDifferenceMm);
        Assert.Equal(geometry.Face0RunMm, geometry.Face1RunMm, 8);
    }

    [Fact]
    public void MirrorFromSeededAsymmetric_ProducesTheOppositePhysicalOrientation()
    {
        var footprint = Rectangle();
        var original = Solve(footprint, 20d, 35d, 1d, 0d, 450d, RoofKind.AsymmetricGable);
        var viewModel = new GableRoofGeometryViewModel(footprint, RoofKind.AsymmetricGable);
        viewModel.SeedFromExistingGeometry(original);

        viewModel.IsAsymmetryMirrored = true;

        Assert.True(viewModel.IsAsymmetryMirrored);
        Assert.True(viewModel.TryGetGeometry(out var mirrored));
        Assert.Equal(35d, mirrored!.Face0SlopeDegrees);
        Assert.Equal(20d, mirrored.Face1SlopeDegrees);
        Assert.Equal(-450d, mirrored.EaveHeightDifferenceMm);
        Assert.NotEqual(original.Signature, mirrored.Signature);
    }

    private static SimpleGableRoofGeometry Solve(
        RoofFootprint footprint,
        double slope0,
        double slope1,
        double directionX,
        double directionY,
        double eaveHeightDifferenceMm,
        RoofKind kind)
    {
        Assert.True(RoofDirection2D.TryCreate(directionX, directionY, out var direction));
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(
                slope0,
                direction,
                Face1SlopeDegrees: slope1,
                EaveHeightDifferenceMm: eaveHeightDifferenceMm),
            kind));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Rectangle()
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(
        [
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(10000d, 0d),
            new RoofPoint2D(10000d, 6000d),
            new RoofPoint2D(0d, 6000d),
        ], true, false, true));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }
}
