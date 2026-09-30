using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class HipRoofPhysicalElevationViewModelTests
{
    [Fact]
    public void Create_DefaultsToEaveModeZeroAndPhysical3DEnabledForRectangle()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        var viewModel = new HipRoofPreviewViewModel(Rectangle(), 45d, HipRoofDialogMode.Create, culture);
        Assert.True(viewModel.SupportsPhysicalElevation);
        Assert.True(viewModel.IsEaveElevationMode);
        Assert.True(viewModel.Physical3DEnabled);
        Assert.True(viewModel.ShowDisplayVisibilityOptions);
        Assert.True(viewModel.IsDisplayBoth);
        Assert.Equal("±0,000", viewModel.ElevationText);
        Assert.Contains("+3,000", viewModel.CalculatedElevationText);
        Assert.True(viewModel.TryGetElevationState(out var state));
        Assert.Equal(0d, state!.ResolvedEaveRelativeElevationMm);
        Assert.Equal(3000d, state.ResolvedRidgeRelativeElevationMm, 9);
        Assert.Equal(RoofPhysicalDisplayVisibility.Both, state.DisplayVisibility);
    }

    [Fact]
    public void DisplayVisibility_PersistsIntoElevationStateIndependentlyOfPhysical3DFlag()
    {
        var viewModel = new HipRoofPreviewViewModel(
            Rectangle(),
            45d,
            HipRoofDialogMode.Create,
            CultureInfo.GetCultureInfo("en-US"))
        {
            IsDisplayPlan2D = true,
        };
        Assert.True(viewModel.IsDisplayPlan2D);
        Assert.True(viewModel.TryGetElevationState(out var state));
        Assert.Equal(RoofPhysicalDisplayVisibility.Plan2D, state!.DisplayVisibility);
        Assert.True(state.Physical3DEnabled);
    }

    [Fact]
    public void ModeSwitch_ConvertsEnteredValueWithoutMovingResolvedElevations()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var viewModel = new HipRoofPreviewViewModel(Rectangle(), 45d, HipRoofDialogMode.Create, culture)
        {
            ElevationText = "+3.000",
        };
        Assert.True(viewModel.TryGetElevationState(out var before));
        viewModel.IsEaveElevationMode = false;
        Assert.True(viewModel.IsRidgeElevationMode);
        Assert.Equal("+6.000", viewModel.ElevationText);
        Assert.True(viewModel.TryGetElevationState(out var after));
        Assert.Equal(before!.ResolvedEaveRelativeElevationMm, after!.ResolvedEaveRelativeElevationMm, 12);
        Assert.Equal(before.ResolvedRidgeRelativeElevationMm, after.ResolvedRidgeRelativeElevationMm, 12);
    }

    [Fact]
    public void LegacyEditSeed_DefaultsPhysical3DDisabled()
    {
        var seed = RoofPhysicalElevationRules.MissingStoreDefault(3000d);
        var viewModel = new HipRoofPreviewViewModel(
            Rectangle(),
            45d,
            HipRoofDialogMode.Edit,
            seed,
            CultureInfo.GetCultureInfo("en-US"));
        Assert.False(viewModel.Physical3DEnabled);
        Assert.True(viewModel.TryGetElevationState(out var state));
        Assert.False(state!.Physical3DEnabled);
    }

    [Fact]
    public void LShape_ExposesOrdinaryPhysicalElevation_FromSolvedTopologyRise()
    {
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(8000, 0), new(8000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        };
        var viewModel = new HipRoofPreviewViewModel(
            Validate(points),
            30d,
            HipRoofDialogMode.Create,
            CultureInfo.GetCultureInfo("en-US"));
        Assert.True(viewModel.SupportsPhysicalElevation);
        Assert.True(viewModel.Physical3DEnabled);
        Assert.True(viewModel.TryGetElevationState(out var state));
        Assert.True(viewModel.TryGetRoofGeometry(out var geometry));
        Assert.Equal(0d, state!.ResolvedEaveRelativeElevationMm);
        Assert.Equal(geometry!.RiseMm, state.ResolvedRidgeRelativeElevationMm, 6);
    }

    [Fact]
    public void ExactZeroFormatting_UsesPlusMinus()
    {
        Assert.Equal(
            "±0.000 m",
            RoofAbsoluteElevationRules.FormatMetresWithUnit(0d, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(
            "Generate physical 3D roof",
            UiStrings.GetString("RoofHip_Physical3DEnabled", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(
            "Both",
            UiStrings.GetString("RoofHip_DisplayBoth", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(
            "2D plan",
            UiStrings.GetString("RoofHip_DisplayPlan2D", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(
            "3D model",
            UiStrings.GetString("RoofHip_DisplayModel3D", CultureInfo.GetCultureInfo("en-US")));
    }

    private static RoofFootprint Rectangle() =>
        Validate(new[]
        {
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(0, 6000),
        });

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(result.IsValid);
        return result.Footprint!;
    }
}
