namespace AcKrovy.Core.Models;

public sealed record TimberAnnotationSettingsPatch(
    TimberAnnotationMode AnnotationMode,
    ItemNumberLeaderStyle ItemNumberLeaderStyle,
    TimberAnnotationScaleOverridePatch AnnotationScaleOverride,
    TimberAnnotationTextSettingsPatch AnnotationTextSettings)
{
    public TimberAnnotationSettingsPatch(
        TimberAnnotationMode annotationMode,
        ItemNumberLeaderStyle itemNumberLeaderStyle,
        TimberAnnotationScaleOverridePatch annotationScaleOverride)
        : this(
            annotationMode,
            itemNumberLeaderStyle,
            annotationScaleOverride,
            TimberAnnotationTextSettingsPatch.Unchanged)
    {
    }
}
