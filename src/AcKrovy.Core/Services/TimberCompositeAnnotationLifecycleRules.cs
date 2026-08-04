using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberCompositeAnnotationLifecycleRules
{
    private static readonly HashSet<TimberMainAnnotationComponentRole>
        PrimaryOnly = new HashSet<TimberMainAnnotationComponentRole>
        {
            TimberMainAnnotationComponentRole.Primary,
        };

    private static readonly HashSet<TimberMainAnnotationComponentRole>
        FramedG4Standalone = new HashSet<TimberMainAnnotationComponentRole>
        {
            TimberMainAnnotationComponentRole.CircleLeaderLine,
            TimberMainAnnotationComponentRole.CircleFrame,
            TimberMainAnnotationComponentRole.CircleText,
        };

    private static readonly HashSet<TimberMainAnnotationComponentRole>
        DimensionsWithFramedItem = new HashSet<TimberMainAnnotationComponentRole>
        {
            TimberMainAnnotationComponentRole.Primary,
            TimberMainAnnotationComponentRole.FramedItem,
        };

    private static readonly HashSet<TimberMainAnnotationComponentRole>
        DimensionsWithFramedG4 = new HashSet<TimberMainAnnotationComponentRole>
        {
            TimberMainAnnotationComponentRole.Primary,
            TimberMainAnnotationComponentRole.CircleLeaderLine,
            TimberMainAnnotationComponentRole.CircleFrame,
            TimberMainAnnotationComponentRole.CircleText,
        };

    public static IReadOnlyCollection<TimberMainAnnotationComponentRole> RequiredRoles(
        TimberAnnotationMode mode) =>
        RequiredRoles(mode, ItemNumberLeaderStyle.Plain);

    public static IReadOnlyCollection<TimberMainAnnotationComponentRole> RequiredRoles(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle itemStyle)
    {
        var normalizedMode = TimberAnnotationModeRules.Normalize(mode);
        var framed = TimberAnnotationModeRules.IsFramedItemLeader(mode, itemStyle);
        if (normalizedMode == TimberAnnotationMode.DimensionsWithItemNumber)
        {
            return framed ? DimensionsWithFramedG4 : DimensionsWithFramedItem;
        }

        if (normalizedMode == TimberAnnotationMode.ItemNumberLeader && framed)
        {
            return FramedG4Standalone;
        }

        return PrimaryOnly;
    }

    public static IReadOnlyList<string> SelectUnexpectedComponentKeys(
        TimberAnnotationMode mode,
        IReadOnlyList<TimberElementLabelCandidate> candidates) =>
        SelectUnexpectedComponentKeys(
            mode,
            ItemNumberLeaderStyle.Plain,
            candidates);

    public static IReadOnlyList<string> SelectUnexpectedComponentKeys(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle itemStyle,
        IReadOnlyList<TimberElementLabelCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }
        var required = RequiredRoles(mode, itemStyle);
        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.LabelKey) &&
                !required.Contains(candidate.ComponentRole))
            .Select(candidate => candidate.LabelKey)
            .ToArray();
    }

    public static bool ContainsItemNumber(
        TimberAnnotationMode mode,
        TimberMainAnnotationComponentRole role) =>
        TimberAnnotationModeRules.Normalize(mode) switch
        {
            TimberAnnotationMode.ItemNumberLeader =>
                role is TimberMainAnnotationComponentRole.Primary or
                    TimberMainAnnotationComponentRole.CircleText,
            TimberAnnotationMode.DimensionsWithItemNumber =>
                role is TimberMainAnnotationComponentRole.FramedItem or
                    TimberMainAnnotationComponentRole.CircleText,
            TimberAnnotationMode.FullLabel =>
                role == TimberMainAnnotationComponentRole.Primary,
            _ => false,
        };
}
