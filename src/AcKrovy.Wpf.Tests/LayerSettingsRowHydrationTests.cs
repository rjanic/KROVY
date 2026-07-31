using System.Globalization;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class LayerSettingsRowHydrationTests
{
    [Fact]
    public void ExplicitLayerValidation_StillRejectsEmptyName()
    {
        Assert.False(LayerNameValidator.TryValidate(
            string.Empty,
            out _,
            out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void EmptyInitialLayerName_UsesCentralElementDefault()
    {
        var row = new LayerSettingsRow(
            TimberElementType.Rafter,
            "Rafter",
            string.Empty,
            LayerColorOption.Create(91, CultureInfo.GetCultureInfo("en-US")),
            CadLinetypeNames.Continuous,
            "0.75");

        Assert.Equal("KROKVA", row.LayerName);
        Assert.Equal(91, row.AciColorIndex);
        Assert.Equal(CadLinetypeNames.Continuous, row.SelectedLinetypeName);
        Assert.Equal("0.75", row.LinetypeScaleText);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerPreset_HydratesActualAciLinetypeAndUniformScale()
    {
        var row = CreateRow();
        var preset = CreatePreset();

        row.HydrateFromExistingLayer(
            preset,
            LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US")),
            "0.75",
            0.75);

        Assert.Equal("KROKVA", row.SelectedExistingLayerName);
        Assert.Equal(90, row.AciColorIndex);
        Assert.Equal(CadLinetypeNames.Continuous, row.SelectedLinetypeName);
        Assert.Equal("0.75", row.LinetypeScaleText);
        Assert.Equal(0.75, row.LoadedEntityLinetypeScale);
        Assert.False(row.HasLayerPropertyOverrides);
        Assert.False(row.HasExplicitPropertyChanges);
    }

    [Fact]
    [Trait("Feature", "ExistingLayerHydration")]
    public void ExistingLayerPreset_SelectionUpdatesExactLayerName()
    {
        var row = CreateRow();
        row.LayerName = "MANUAL_NEW_LAYER";

        Hydrate(
            row,
            CreatePreset(),
            LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US")));

        Assert.Equal("KROKVA", row.LayerName);
        Assert.Equal("KROKVA", row.SelectedExistingLayerName);
        Assert.False(row.HasExplicitPropertyChanges);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void ExistingLayerPreset_TracksAciLinetypeAndScaleOverridesIndependently()
    {
        var row = CreateRow();
        var preset = CreatePreset();
        var loadedColor = LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US"));

        Hydrate(row, preset, loadedColor);
        row.AciColorIndex = 91;
        Assert.True(row.HasLayerPropertyOverrides);

        Hydrate(row, preset, loadedColor);
        row.SelectedLinetypeName = CadLinetypeNames.DashDot;
        Assert.True(row.HasLayerPropertyOverrides);

        Hydrate(row, preset, loadedColor);
        row.LinetypeScaleText = "0.8";
        Assert.True(row.HasLayerPropertyOverrides);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void ExistingLayerPreset_UsesScaleToleranceAndManualNameClearsBaseline()
    {
        var row = CreateRow();
        var preset = CreatePreset();
        Hydrate(
            row,
            preset,
            LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US")));

        row.LinetypeScaleText =
            (0.75 + CadLayerScaleHydrationRules.ComparisonTolerance / 2d)
            .ToString("R", CultureInfo.InvariantCulture);
        Assert.False(row.HasLayerPropertyOverrides);

        row.LayerName = "MANUAL_NEW_LAYER";
        Assert.Null(row.SelectedExistingLayerName);
        Assert.False(row.HasLayerPropertyOverrides);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void EnteredLayer_TracksExplicitAciLinetypeAndScaleChanges()
    {
        var colorRow = CreateEnteredRow();
        colorRow.AciColorIndex = 91;
        Assert.True(colorRow.HasExplicitPropertyChanges);

        var linetypeRow = CreateEnteredRow();
        linetypeRow.SelectedLinetypeName = CadLinetypeNames.Continuous;
        Assert.True(linetypeRow.HasExplicitPropertyChanges);

        var scaleRow = CreateEnteredRow();
        scaleRow.LinetypeScaleText = "0.75";
        Assert.True(scaleRow.HasExplicitPropertyChanges);
    }

    [Fact]
    [Trait("Feature", "SuffixApply")]
    public void AcceptedExistingLayer_ResetsExplicitPropertyChanges()
    {
        var row = CreateRow();
        Hydrate(
            row,
            CreatePreset(),
            LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US")));
        row.AciColorIndex = 91;
        Assert.True(row.HasExplicitPropertyChanges);

        Hydrate(
            row,
            CreatePreset(),
            LayerColorOption.Create(90, CultureInfo.GetCultureInfo("en-US")));

        Assert.False(row.HasExplicitPropertyChanges);
    }

    private static LayerSettingsRow CreateRow() => new(
        TimberElementType.Rafter,
        "Rafter",
        "KROKVA",
        LayerColorOption.Create(2, CultureInfo.GetCultureInfo("en-US")),
        CadLinetypeNames.DashDot,
        "0.5");

    private static LayerSettingsRow CreateEnteredRow() => new(
        TimberElementType.Rafter,
        "Rafter",
        "MANUAL_NEW_LAYER",
        LayerColorOption.Create(2, CultureInfo.GetCultureInfo("en-US")),
        CadLinetypeNames.DashDot,
        "0.5");

    private static CadLayerPreset CreatePreset() =>
        new("KROKVA", 90, CadLinetypeNames.Continuous, 0.75);

    private static void Hydrate(
        LayerSettingsRow row,
        CadLayerPreset preset,
        LayerColorOption color) =>
        row.HydrateFromExistingLayer(preset, color, "0.75", 0.75);
}
