using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using System.Globalization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class MonopitchRoofGeometryViewModelTests
{
    [Fact]
    public void NewMonopitchEditor_RequiresAnExplicitDirectionBeforePreviewOrCreate()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(), RoofKind.Monopitch);

        Assert.False(viewModel.HasRidgeDirection);
        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryGetRoofGeometry(out _));
        Assert.NotEmpty(viewModel.ValidationMessage);
    }

    [Fact]
    public void SlopeMode_DerivesHeightFromDirectedSpanWithoutUsingDisplayValue()
    {
        var viewModel = CreateViewModel();
        viewModel.AlphaText = "30";

        Assert.True(viewModel.TryGetRoofGeometry(out var result));
        var geometry = Assert.IsType<MonopitchRoofGeometry>(result);
        Assert.Equal(6000d, geometry.SpanMm, 9);
        Assert.Equal(6000d * Math.Tan(Math.PI / 6d), geometry.EaveHeightDifferenceMm, 9);
        Assert.Equal("3464", viewModel.EaveHeightDifferenceText);
        Assert.NotNull(viewModel.MonopitchSectionState);
    }

    [Fact]
    public void HeightMode_DerivesSlopeAndKeepsHeightAsAuthoritativeInput()
    {
        var viewModel = CreateViewModel();
        viewModel.IsMonopitchHeightMode = true;
        viewModel.EaveHeightDifferenceText = "3000";

        Assert.True(viewModel.TryGetRoofGeometry(out var result));
        var geometry = Assert.IsType<MonopitchRoofGeometry>(result);
        Assert.Equal(3000d, geometry.EaveHeightDifferenceMm, 9);
        Assert.Equal(Math.Atan2(3000d, 6000d) * 180d / Math.PI, geometry.SlopeDegrees, 9);
    }

    [Fact]
    public void MirrorToggle_ReversesLowHighAndSecondToggleRestoresSignature()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.TryGetRoofGeometry(out var initialResult));
        var initial = Assert.IsType<MonopitchRoofGeometry>(initialResult);

        viewModel.IsMonopitchMirrored = true;
        Assert.True(viewModel.TryGetRoofGeometry(out var mirroredResult));
        var mirrored = Assert.IsType<MonopitchRoofGeometry>(mirroredResult);
        Assert.Equal(-initial.LowToHighDirection.X, mirrored.LowToHighDirection.X, 10);
        Assert.Equal(-initial.LowToHighDirection.Y, mirrored.LowToHighDirection.Y, 10);
        Assert.Equal(initial.EaveHeightDifferenceMm, mirrored.EaveHeightDifferenceMm, 9);

        viewModel.IsMonopitchMirrored = false;
        Assert.True(viewModel.TryGetRoofGeometry(out var restoredResult));
        Assert.Equal(initial.Signature, Assert.IsType<MonopitchRoofGeometry>(restoredResult).Signature);
    }

    [Fact]
    public void EditSeed_ReproducesPersistedPhysicalGeometryAndLocksRoofFamily()
    {
        var original = Solve(37d);
        var viewModel = new GableRoofGeometryViewModel(Rectangle(), RoofKind.Monopitch);

        viewModel.SeedFromExistingGeometry(original);

        Assert.True(viewModel.IsMonopitchEditor);
        Assert.False(viewModel.IsGableEditor);
        viewModel.SelectedKind = RoofKind.SimpleGable;
        Assert.Equal(RoofKind.Monopitch, viewModel.SelectedKind);
        Assert.True(viewModel.TryGetRoofGeometry(out var result));
        Assert.Equal(original.Signature, Assert.IsType<MonopitchRoofGeometry>(result).Signature);
    }

    [Theory]
    [InlineData("sk", "VYSOKÝ", "NÍZKY")]
    [InlineData("cs", "VYSOKÝ", "NÍZKÝ")]
    [InlineData("en", "HIGH", "LOW")]
    [InlineData("de", "HOCH", "NIEDRIG")]
    [InlineData("pl", "WYSOKI", "NISKI")]
    [InlineData("fr", "HAUT", "BAS")]
    public void Editor_ShowsPhysicalHighToLowFallDirectionInEveryLanguage(
        string cultureName,
        string high,
        string low)
    {
        var viewModel = new GableRoofGeometryViewModel(
            Rectangle(),
            RoofKind.Monopitch,
            CultureInfo.GetCultureInfo(cultureName));
        viewModel.SetRidgeDirection(Direction(0d, 1d));

        Assert.Contains(high, viewModel.OrientationDirectionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(low, viewModel.OrientationDirectionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(high, viewModel.PickOrientationDirectionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(low, viewModel.PickOrientationDirectionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-1", viewModel.OrientationDirectionText, StringComparison.Ordinal);
    }

    private static GableRoofGeometryViewModel CreateViewModel()
    {
        var viewModel = new GableRoofGeometryViewModel(Rectangle(), RoofKind.Monopitch);
        viewModel.SetRidgeDirection(Direction(0d, 1d));
        return viewModel;
    }

    private static MonopitchRoofGeometry Solve(double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            Rectangle(),
            new RoofParameters(slope, SlopeDirection: Direction(0d, 1d)),
            RoofKind.Monopitch));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
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
        return result.Footprint!;
    }
}
