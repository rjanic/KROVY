using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

public static class TimberAnnotationModeDisplayNameProvider
{
    public static string GetDisplayName(
        TimberAnnotationMode mode,
        CultureInfo? culture = null) =>
        UiStrings.GetString(
            mode switch
            {
                TimberAnnotationMode.FullLabel => "AnnotationMode_FullLabel",
                TimberAnnotationMode.ItemNumberLeader => "AnnotationMode_ItemNumberLeader",
                TimberAnnotationMode.DimensionsLeader => "AnnotationMode_DimensionsLeader",
                TimberAnnotationMode.NoAnnotations => "AnnotationMode_NoAnnotations",
                TimberAnnotationMode.DimensionsWithItemNumber =>
                    "AnnotationMode_DimensionsLeader",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
            },
            culture);
}
