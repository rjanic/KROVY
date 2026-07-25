using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.Localization;

public sealed class LocalizedOption<T>
{
    public LocalizedOption(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public T Value { get; }
    public string DisplayName { get; }
}

public static class SettingsLocalizedOptionCatalog
{
    public static IReadOnlyList<LocalizedOption<TimberAnnotationMode>> AnnotationModes(
        CultureInfo culture) =>
        Enum.GetValues(typeof(TimberAnnotationMode))
            .Cast<TimberAnnotationMode>()
            .Select(mode => new LocalizedOption<TimberAnnotationMode>(
                mode,
                TimberAnnotationModeDisplayNameProvider.GetDisplayName(mode, culture)))
            .ToList();

    public static IReadOnlyList<LocalizedOption<ItemNumberLeaderStyle>> ItemNumberLeaderStyles(
        CultureInfo culture) =>
        Enum.GetValues(typeof(ItemNumberLeaderStyle))
            .Cast<ItemNumberLeaderStyle>()
            .Select(ItemNumberLeaderStyleRules.Normalize)
            .Distinct()
            .Select(style => new LocalizedOption<ItemNumberLeaderStyle>(
                style,
                ItemNumberLeaderStyleDisplayNameProvider.GetDisplayName(style, culture)))
            .ToList();

    public static IReadOnlyList<LocalizedOption<SettingsSaveMode>> ApplyModes(
        CultureInfo culture) =>
    [
        new(
            SettingsSaveMode.NewElementsOnly,
            UiStrings.GetString("SettingsWindow_SaveNewElementsOnly", culture)),
        new(
            SettingsSaveMode.SelectedElements,
            UiStrings.GetString("SettingsWindow_SaveApplySelection", culture)),
        new(
            SettingsSaveMode.AllElements,
            UiStrings.GetString("SettingsWindow_SaveApplyAll", culture)),
    ];
}

public sealed class SettingsLocalizedSelectionSet
{
    private SettingsLocalizedSelectionSet(
        IReadOnlyList<LocalizedOption<TimberAnnotationMode>> annotationModes,
        IReadOnlyList<LocalizedOption<ItemNumberLeaderStyle>> itemNumberLeaderStyles,
        IReadOnlyList<LocalizedOption<SettingsSaveMode>> applyModes,
        TimberAnnotationMode selectedAnnotationMode,
        ItemNumberLeaderStyle selectedItemNumberLeaderStyle,
        SettingsSaveMode selectedApplyMode)
    {
        AnnotationModes = annotationModes;
        ItemNumberLeaderStyles = itemNumberLeaderStyles;
        ApplyModes = applyModes;
        SelectedAnnotationMode = selectedAnnotationMode;
        SelectedItemNumberLeaderStyle = selectedItemNumberLeaderStyle;
        SelectedApplyMode = selectedApplyMode;
    }

    public IReadOnlyList<LocalizedOption<TimberAnnotationMode>> AnnotationModes { get; }
    public IReadOnlyList<LocalizedOption<ItemNumberLeaderStyle>> ItemNumberLeaderStyles { get; }
    public IReadOnlyList<LocalizedOption<SettingsSaveMode>> ApplyModes { get; }
    public TimberAnnotationMode SelectedAnnotationMode { get; }
    public ItemNumberLeaderStyle SelectedItemNumberLeaderStyle { get; }
    public SettingsSaveMode SelectedApplyMode { get; }

    public static SettingsLocalizedSelectionSet Create(
        CultureInfo culture,
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        SettingsSaveMode applyMode)
    {
        var annotationModes = SettingsLocalizedOptionCatalog.AnnotationModes(culture);
        var itemStyles = SettingsLocalizedOptionCatalog.ItemNumberLeaderStyles(culture);
        var applyModes = SettingsLocalizedOptionCatalog.ApplyModes(culture);
        var normalizedAnnotation = SettingsSelectionRules.NormalizeAnnotationMode(annotationMode);
        var normalizedItemStyle =
            SettingsSelectionRules.NormalizeItemNumberLeaderStyle(itemNumberLeaderStyle);
        var normalizedApplyMode = SettingsSelectionRules.NormalizeApplyMode(applyMode);

        return new SettingsLocalizedSelectionSet(
            annotationModes,
            itemStyles,
            applyModes,
            annotationModes.Any(option => option.Value == normalizedAnnotation)
                ? normalizedAnnotation
                : TimberAnnotationMode.FullLabel,
            itemStyles.Any(option => option.Value == normalizedItemStyle)
                ? normalizedItemStyle
                : ItemNumberLeaderStyle.Plain,
            applyModes.Any(option => option.Value == normalizedApplyMode)
                ? normalizedApplyMode
                : SettingsSaveMode.NewElementsOnly);
    }
}
