using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberAnnotationModeRules
{
    public static TimberAnnotationMode Normalize(TimberAnnotationMode mode) =>
        Enum.IsDefined(typeof(TimberAnnotationMode), mode)
            ? mode
            : TimberAnnotationMode.FullLabel;

    public static TimberMainAnnotationRepresentation GetRepresentation(
        TimberAnnotationMode mode) =>
        Normalize(mode) switch
        {
            TimberAnnotationMode.FullLabel => TimberMainAnnotationRepresentation.FullLabel,
            TimberAnnotationMode.NoAnnotations => TimberMainAnnotationRepresentation.None,
            _ => TimberMainAnnotationRepresentation.Leader,
        };

    public static TimberMainAnnotationRepresentation GetRepresentation(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style) =>
        Normalize(mode) == TimberAnnotationMode.ItemNumberLeader &&
        ItemNumberLeaderStyleRules.Normalize(style) is
            ItemNumberLeaderStyle.Circle or
            ItemNumberLeaderStyle.Slot or
            ItemNumberLeaderStyle.Rectangle
            ? TimberMainAnnotationRepresentation.BlockLeader
            : GetRepresentation(mode);

    public static bool IsFramedItemLeader(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style) =>
        (Normalize(mode) == TimberAnnotationMode.ItemNumberLeader ||
         Normalize(mode) == TimberAnnotationMode.DimensionsWithItemNumber) &&
        ItemNumberLeaderStyleRules.Normalize(style) is
            ItemNumberLeaderStyle.Circle or
            ItemNumberLeaderStyle.Slot or
            ItemNumberLeaderStyle.Rectangle;

    public static bool RequiresReplacement(
        TimberMainAnnotationRepresentation existing,
        TimberAnnotationMode desiredMode) =>
        existing != GetRepresentation(desiredMode);

    public static bool RequiresItemLeaderReplacement(
        ItemNumberLeaderStyle existingStyle,
        ItemNumberLeaderStyle desiredStyle) =>
        ItemNumberLeaderStyleRules.Normalize(existingStyle) !=
            ItemNumberLeaderStyleRules.Normalize(desiredStyle);

    public static bool RequiresLeaderRecreation(
        TimberAnnotationMode existingMode,
        ItemNumberLeaderStyle existingStyle,
        TimberAnnotationMode desiredMode,
        ItemNumberLeaderStyle desiredStyle)
    {
        var normalizedExistingMode = Normalize(existingMode);
        var normalizedDesiredMode = Normalize(desiredMode);
        if (normalizedExistingMode != normalizedDesiredMode)
        {
            return true;
        }

        return (normalizedDesiredMode is
            TimberAnnotationMode.ItemNumberLeader or
            TimberAnnotationMode.DimensionsWithItemNumber) &&
            RequiresItemLeaderReplacement(existingStyle, desiredStyle);
    }
}
