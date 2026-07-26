using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class SettingsAnnotationPresetRules
{
    public static IReadOnlyList<SettingsAnnotationPresetDefinition> All { get; } =
    [
        Define(SettingsAnnotationPreset.NoAnnotations, "no-annotations",
            "01_bez_popisov.png",
            "SettingsAnnotationPreset_NoAnnotations",
            TimberAnnotationMode.NoAnnotations, ItemNumberLeaderStyle.Plain),
        Define(SettingsAnnotationPreset.ItemPlain, "item-plain",
            "02_polozka_plain.png",
            "SettingsAnnotationPreset_ItemPlain",
            TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Plain),
        Define(SettingsAnnotationPreset.ItemCircle, "item-circle",
            "03_polozka_kruh.png",
            "SettingsAnnotationPreset_ItemCircle",
            TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Circle),
        Define(SettingsAnnotationPreset.ItemRectangle, "item-rectangle",
            "04_polozka_obdlznik.png",
            "SettingsAnnotationPreset_ItemRectangle",
            TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Rectangle),
        Define(SettingsAnnotationPreset.ItemSlot, "item-slot",
            "05_polozka_slot.png",
            "SettingsAnnotationPreset_ItemSlot",
            TimberAnnotationMode.ItemNumberLeader, ItemNumberLeaderStyle.Slot),
        Define(SettingsAnnotationPreset.DimensionsOnly, "dimensions-only",
            "06_iba_rozmery.png",
            "SettingsAnnotationPreset_DimensionsOnly",
            TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain),
        Define(SettingsAnnotationPreset.FullLabel, "full-label",
            "07_kompletny_popis.png",
            "SettingsAnnotationPreset_FullLabel",
            TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain),
        Define(SettingsAnnotationPreset.DimensionsWithItemCircle, "dimensions-item-circle",
            "08_rozmery_kruh.png",
            "SettingsAnnotationPreset_DimensionsWithItemCircle",
            TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Circle),
        Define(SettingsAnnotationPreset.DimensionsWithItemRectangle, "dimensions-item-rectangle",
            "09_rozmery_obdlznik.png",
            "SettingsAnnotationPreset_DimensionsWithItemRectangle",
            TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Rectangle),
        Define(SettingsAnnotationPreset.DimensionsWithItemSlot, "dimensions-item-slot",
            "10_rozmery_slot.png",
            "SettingsAnnotationPreset_DimensionsWithItemSlot",
            TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Slot),
    ];

    public static SettingsAnnotationPresetDefinition Get(SettingsAnnotationPreset preset) =>
        All.FirstOrDefault(candidate => candidate.Preset == preset) ??
        All.Single(candidate => candidate.Preset == SettingsAnnotationPreset.FullLabel);

    public static SettingsAnnotationPreset FromProduction(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style)
    {
        var normalizedMode = TimberAnnotationModeRules.Normalize(mode);
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        return All.FirstOrDefault(candidate =>
                candidate.AnnotationMode == normalizedMode &&
                (normalizedMode is TimberAnnotationMode.ItemNumberLeader or
                    TimberAnnotationMode.DimensionsWithItemNumber
                        ? candidate.ItemNumberLeaderStyle == normalizedStyle
                        : true))
            ?.Preset ?? SettingsAnnotationPreset.FullLabel;
    }

    private static SettingsAnnotationPresetDefinition Define(
        SettingsAnnotationPreset preset,
        string stableId,
        string referenceFileName,
        string localizationKey,
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style) =>
        new(
            preset,
            stableId,
            referenceFileName,
            localizationKey,
            mode,
            style);
}
