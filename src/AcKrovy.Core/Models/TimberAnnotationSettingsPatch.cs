namespace AcKrovy.Core.Models;

public sealed record TimberAnnotationSettingsPatch(
    TimberAnnotationMode AnnotationMode,
    ItemNumberLeaderStyle ItemNumberLeaderStyle,
    TimberAnnotationScaleOverridePatch AnnotationScaleOverride);
