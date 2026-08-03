using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public enum TimberAnnotationSettingsApplyScope
{
    NewElementsOnly = 0,
    SelectedElements = 1,
    AllElements = 2,
}

/// <summary>
/// One CAD-neutral request for every setting in the Annotation section.
/// Future text, line, post and tag settings extend this contract while keeping
/// the same three apply scopes.
/// </summary>
public sealed record TimberAnnotationSettingsRequest
{
    public TimberAnnotationSettingsRequest(
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        int scaleDenominator,
        TimberAnnotationSettingsApplyScope applyScope)
        : this(
            annotationMode,
            itemNumberLeaderStyle,
            scaleDenominator,
            applyScope,
            annotationTextSettings: null,
            allowMissingTextSettings: true,
            annotationTextPatch: TimberAnnotationTextSettingsPatch.Unchanged)
    {
    }

    public TimberAnnotationSettingsRequest(
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        int scaleDenominator,
        TimberAnnotationSettingsApplyScope applyScope,
        TimberAnnotationTextSettings annotationTextSettings)
        : this(
            annotationMode,
            itemNumberLeaderStyle,
            scaleDenominator,
            applyScope,
            annotationTextSettings,
            allowMissingTextSettings: false,
            annotationTextPatch: TimberAnnotationTextSettingsPatch.Unchanged)
    {
    }

    public TimberAnnotationSettingsRequest(
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        int scaleDenominator,
        TimberAnnotationSettingsApplyScope applyScope,
        TimberAnnotationTextSettings annotationTextSettings,
        TimberAnnotationTextSettingsPatch annotationTextPatch)
        : this(
            annotationMode,
            itemNumberLeaderStyle,
            scaleDenominator,
            applyScope,
            annotationTextSettings,
            allowMissingTextSettings: false,
            annotationTextPatch: annotationTextPatch ??
                throw new ArgumentNullException(nameof(annotationTextPatch)))
    {
    }

    private TimberAnnotationSettingsRequest(
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        int scaleDenominator,
        TimberAnnotationSettingsApplyScope applyScope,
        TimberAnnotationTextSettings? annotationTextSettings,
        bool allowMissingTextSettings,
        TimberAnnotationTextSettingsPatch annotationTextPatch)
    {
        if (!TimberAnnotationScaleRules.IsValidDenominator(scaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleDenominator),
                scaleDenominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }
        if (applyScope is not
            (TimberAnnotationSettingsApplyScope.NewElementsOnly or
             TimberAnnotationSettingsApplyScope.SelectedElements or
             TimberAnnotationSettingsApplyScope.AllElements))
        {
            throw new ArgumentOutOfRangeException(nameof(applyScope), applyScope, null);
        }

        AnnotationMode = TimberAnnotationModeRules.Normalize(annotationMode);
        ItemNumberLeaderStyle = ItemNumberLeaderStyleRules.Normalize(itemNumberLeaderStyle);
        ScaleDenominator = scaleDenominator;
        ApplyScope = applyScope;
        AnnotationTextSettings = allowMissingTextSettings
            ? null
            : TimberAnnotationTextSettingsRules.ValidateAndNormalize(
                annotationTextSettings ??
                throw new ArgumentNullException(nameof(annotationTextSettings)));
        AnnotationTextPatch = annotationTextPatch;
    }

    public TimberAnnotationMode AnnotationMode { get; }
    public ItemNumberLeaderStyle ItemNumberLeaderStyle { get; }
    public int ScaleDenominator { get; }
    public TimberAnnotationSettingsApplyScope ApplyScope { get; }
    public TimberAnnotationTextSettings? AnnotationTextSettings { get; }
    public TimberAnnotationTextSettingsPatch AnnotationTextPatch { get; }

    public TimberAnnotationSettingsPatch CreateElementPatch() => new(
        AnnotationMode,
        ItemNumberLeaderStyle,
        ApplyScope switch
        {
            TimberAnnotationSettingsApplyScope.SelectedElements =>
                TimberAnnotationScaleOverridePatch.Set(ScaleDenominator),
            TimberAnnotationSettingsApplyScope.AllElements =>
                TimberAnnotationScaleOverridePatch.Clear,
            _ => TimberAnnotationScaleOverridePatch.Unchanged,
        },
        ResolveTextPatch());

    private TimberAnnotationTextSettingsPatch ResolveTextPatch()
    {
        if (ApplyScope == TimberAnnotationSettingsApplyScope.NewElementsOnly)
        {
            return TimberAnnotationTextSettingsPatch.Unchanged;
        }

        if (AnnotationTextPatch.Change != TimberAnnotationTextSettingsChange.Unchanged)
        {
            return AnnotationTextPatch;
        }

        if (AnnotationTextSettings is not null)
        {
            return TimberAnnotationTextSettingsPatch.Set(AnnotationTextSettings);
        }

        return TimberAnnotationTextSettingsPatch.Unchanged;
    }
}
