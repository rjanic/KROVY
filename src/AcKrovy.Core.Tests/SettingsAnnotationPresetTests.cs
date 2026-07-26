using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SettingsAnnotationPresetTests
{
    [Fact]
    [Trait("Feature", "AnnotationPresets")]
    public void Catalog_HasTenStableLocalizedPreviewDefinitions()
    {
        var definitions = SettingsAnnotationPresetRules.All;

        Assert.Equal(10, definitions.Count);
        Assert.Equal(10, definitions.Select(value => value.Preset).Distinct().Count());
        Assert.Equal(10, definitions.Select(value => value.StableId).Distinct().Count());
        Assert.Equal(10, definitions.Select(value => value.ReferenceFileName).Distinct().Count());
        Assert.All(definitions, value => Assert.False(string.IsNullOrWhiteSpace(value.LocalizationKey)));
        Assert.Equal(
            [
                "01_bez_popisov.png",
                "02_polozka_plain.png",
                "03_polozka_kruh.png",
                "04_polozka_obdlznik.png",
                "05_polozka_slot.png",
                "06_iba_rozmery.png",
                "07_kompletny_popis.png",
                "08_rozmery_kruh.png",
                "09_rozmery_obdlznik.png",
                "10_rozmery_slot.png",
            ],
            definitions.Select(value => value.ReferenceFileName));
        Assert.DoesNotContain(
            definitions,
            value => value.ReferenceFileName.Contains(@"C:\", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [Trait("Feature", "AnnotationPresets")]
    [InlineData(SettingsAnnotationPreset.NoAnnotations, TimberAnnotationMode.NoAnnotations, ItemNumberLeaderStyle.Plain)]
    [InlineData(SettingsAnnotationPreset.ItemPlain, TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain)]
    [InlineData(SettingsAnnotationPreset.ItemCircle, TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Circle)]
    [InlineData(SettingsAnnotationPreset.ItemRectangle, TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Rectangle)]
    [InlineData(SettingsAnnotationPreset.ItemSlot, TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Slot)]
    [InlineData(SettingsAnnotationPreset.DimensionsOnly, TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain)]
    [InlineData(SettingsAnnotationPreset.FullLabel, TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain)]
    [InlineData(SettingsAnnotationPreset.DimensionsWithItemCircle, TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Circle)]
    [InlineData(SettingsAnnotationPreset.DimensionsWithItemRectangle, TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Rectangle)]
    [InlineData(SettingsAnnotationPreset.DimensionsWithItemSlot, TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Slot)]
    public void Preset_MapsToProductionModel(
        SettingsAnnotationPreset preset,
        TimberAnnotationMode expectedMode,
        ItemNumberLeaderStyle expectedStyle)
    {
        var definition = SettingsAnnotationPresetRules.Get(preset);

        Assert.Equal(expectedMode, definition.AnnotationMode);
        Assert.Equal(expectedStyle, definition.ItemNumberLeaderStyle);
        Assert.Equal(
            preset,
            SettingsAnnotationPresetRules.FromProduction(expectedMode, expectedStyle));
    }

    [Fact]
    [Trait("Feature", "CombinedAnnotation")]
    public void CombinedMode_RoundTripsWithoutSchemaChangeAndLegacyValuesRemainStable()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            AnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Slot,
        };

        var json = JsonSerializer.Serialize(source, options);
        var loaded = JsonSerializer.Deserialize<TimberElementData>(json, options);

        Assert.NotNull(loaded);
        Assert.Equal(TimberAnnotationMode.DimensionsWithItemNumber, loaded!.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Slot, loaded.ItemNumberLeaderStyle);
        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Equal(0, (int)TimberAnnotationMode.FullLabel);
        Assert.Equal(1, (int)TimberAnnotationMode.ItemNumberLeader);
        Assert.Equal(2, (int)TimberAnnotationMode.DimensionsLeader);
        Assert.Equal(3, (int)TimberAnnotationMode.NoAnnotations);
        Assert.Equal(
            "80x160",
            TimberMainAnnotationFormatter.Format(
                source,
                TimberCalculator.Measure(source, planLengthMm: 1000d)));
    }

    [Fact]
    [Trait("Feature", "CombinedAnnotation")]
    public void CompositeLifecycle_KeepsOnePrimaryAndOneFramedItem()
    {
        var candidates = new[]
        {
            Candidate("primary", TimberMainAnnotationComponentRole.Primary),
            Candidate("framed", TimberMainAnnotationComponentRole.FramedItem),
            Candidate("framed-copy", TimberMainAnnotationComponentRole.FramedItem),
        };

        var duplicates = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            candidates,
            ["1A"]);
        var leavingCombined =
            TimberCompositeAnnotationLifecycleRules.SelectUnexpectedComponentKeys(
                TimberAnnotationMode.FullLabel,
                candidates);

        Assert.Equal(["framed-copy"], duplicates);
        Assert.Equal(["framed", "framed-copy"], leavingCombined);
        Assert.True(TimberCompositeAnnotationLifecycleRules.ContainsItemNumber(
            TimberAnnotationMode.DimensionsWithItemNumber,
            TimberMainAnnotationComponentRole.FramedItem));
        Assert.False(TimberCompositeAnnotationLifecycleRules.ContainsItemNumber(
            TimberAnnotationMode.DimensionsWithItemNumber,
            TimberMainAnnotationComponentRole.Primary));
    }

    [Theory]
    [Trait("Feature", "AnnotationPresets")]
    [InlineData("sk-SK")]
    [InlineData("cs-CZ")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    public void EveryPreset_IsLocalizedInEverySupportedLanguage(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        Assert.All(
            SettingsAnnotationPresetRules.All,
            definition => Assert.False(string.IsNullOrWhiteSpace(
                SettingsAnnotationPresetDisplayNameProvider.GetDisplayName(
                    definition.Preset,
                    culture))));
    }

    private static TimberElementLabelCandidate Candidate(
        string key,
        TimberMainAnnotationComponentRole role) =>
        new()
        {
            LabelKey = key,
            ElementId = "K1",
            SourceHandle = "1A",
            ComponentRole = role,
        };
}
