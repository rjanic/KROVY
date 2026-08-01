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
    }

    public TimberAnnotationMode AnnotationMode { get; }
    public ItemNumberLeaderStyle ItemNumberLeaderStyle { get; }
    public int ScaleDenominator { get; }
    public TimberAnnotationSettingsApplyScope ApplyScope { get; }

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
        });
}
