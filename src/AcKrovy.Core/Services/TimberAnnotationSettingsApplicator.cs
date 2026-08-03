using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationSettingsApplicator
{
    public static TimberElementData Apply(
        TimberElementData source,
        TimberAnnotationSettingsPatch patch)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        if (patch.AnnotationScaleOverride is null)
        {
            throw new ArgumentException(
                "Annotation scale override patch is required.",
                nameof(patch));
        }
        if (patch.AnnotationTextSettings is null)
        {
            throw new ArgumentException(
                "Annotation text settings patch is required.",
                nameof(patch));
        }

        return source with
        {
            AnnotationMode = TimberAnnotationModeRules.Normalize(patch.AnnotationMode),
            ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(
                patch.ItemNumberLeaderStyle),
            AnnotationScaleDenominatorOverride = ApplyScaleOverride(
                source.AnnotationScaleDenominatorOverride,
                patch.AnnotationScaleOverride),
            AnnotationTextSettings = ApplyTextSettings(
                source.AnnotationTextSettings,
                patch.AnnotationTextSettings),
        };
    }

    private static int? ApplyScaleOverride(
        int? currentValue,
        TimberAnnotationScaleOverridePatch patch) =>
        patch.Change switch
        {
            TimberAnnotationScaleOverrideChange.Unchanged => currentValue,
            TimberAnnotationScaleOverrideChange.Set => patch.Denominator,
            TimberAnnotationScaleOverrideChange.Clear => null,
            _ => throw new ArgumentOutOfRangeException(nameof(patch)),
        };

    private static TimberAnnotationTextSettings? ApplyTextSettings(
        TimberAnnotationTextSettings? currentValue,
        TimberAnnotationTextSettingsPatch patch) =>
        patch.Apply(currentValue);
}
