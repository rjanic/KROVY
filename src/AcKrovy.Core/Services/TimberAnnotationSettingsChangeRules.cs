using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral detection of Annotation-section changes that must drive Settings
/// apply independently of annotation scale.
/// </summary>
public static class TimberAnnotationSettingsChangeRules
{
    public static bool HasAnnotationModeChanged(
        TimberAnnotationMode acceptedMode,
        TimberAnnotationMode selectedMode) =>
        TimberAnnotationModeRules.Normalize(acceptedMode) !=
        TimberAnnotationModeRules.Normalize(selectedMode);

    public static bool HasItemNumberLeaderStyleChanged(
        ItemNumberLeaderStyle acceptedStyle,
        ItemNumberLeaderStyle selectedStyle) =>
        ItemNumberLeaderStyleRules.Normalize(acceptedStyle) !=
        ItemNumberLeaderStyleRules.Normalize(selectedStyle);

    public static bool HasPresentationChanged(
        TimberAnnotationMode acceptedMode,
        ItemNumberLeaderStyle acceptedStyle,
        TimberAnnotationMode selectedMode,
        ItemNumberLeaderStyle selectedStyle,
        TimberAnnotationTextSettingsPatch textPatch)
    {
        if (textPatch is null)
        {
            throw new ArgumentNullException(nameof(textPatch));
        }

        return HasAnnotationModeChanged(acceptedMode, selectedMode) ||
            HasItemNumberLeaderStyleChanged(acceptedStyle, selectedStyle) ||
            textPatch.Change != TimberAnnotationTextSettingsChange.Unchanged;
    }

    public static bool HasPresentationChanged(
        TimberAnnotationMode acceptedMode,
        ItemNumberLeaderStyle acceptedStyle,
        TimberAnnotationMode selectedMode,
        ItemNumberLeaderStyle selectedStyle) =>
        HasPresentationChanged(
            acceptedMode,
            acceptedStyle,
            selectedMode,
            selectedStyle,
            TimberAnnotationTextSettingsPatch.Unchanged);

    public static bool HasScaleChanged(
        int acceptedDenominator,
        int selectedDenominator) =>
        TimberAnnotationScaleRules.NormalizeDenominator(acceptedDenominator) !=
        TimberAnnotationScaleRules.NormalizeDenominator(selectedDenominator);

    public static bool ShouldApplyScaleChange(
        TimberAnnotationSettingsApplyScope applyScope,
        int acceptedDrawingDenominator,
        int selectedDenominator) =>
        applyScope is
            TimberAnnotationSettingsApplyScope.SelectedElements or
            TimberAnnotationSettingsApplyScope.AllElements ||
        HasScaleChanged(acceptedDrawingDenominator, selectedDenominator);

    public static bool ShouldRefreshAllEligible(
        bool drawingScaleChanged,
        bool presentationSettingsChanged) =>
        drawingScaleChanged || presentationSettingsChanged;

    public static TimberAnnotationScaleOverridePatch ResolveScaleOverride(
        TimberAnnotationSettingsApplyScope applyScope,
        bool applyScaleChange,
        int scaleDenominator)
    {
        if (!applyScaleChange)
        {
            return TimberAnnotationScaleOverridePatch.Unchanged;
        }

        return applyScope switch
        {
            TimberAnnotationSettingsApplyScope.SelectedElements =>
                TimberAnnotationScaleOverridePatch.Set(scaleDenominator),
            TimberAnnotationSettingsApplyScope.AllElements =>
                TimberAnnotationScaleOverridePatch.Clear,
            _ => TimberAnnotationScaleOverridePatch.Unchanged,
        };
    }
}
