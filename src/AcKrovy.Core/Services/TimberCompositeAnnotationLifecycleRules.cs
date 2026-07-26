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
        DimensionsWithFramedItem = new HashSet<TimberMainAnnotationComponentRole>
        {
            TimberMainAnnotationComponentRole.Primary,
            TimberMainAnnotationComponentRole.FramedItem,
        };

    public static IReadOnlyCollection<TimberMainAnnotationComponentRole> RequiredRoles(
        TimberAnnotationMode mode) =>
        TimberAnnotationModeRules.Normalize(mode) ==
            TimberAnnotationMode.DimensionsWithItemNumber
                ? DimensionsWithFramedItem
                : PrimaryOnly;

    public static IReadOnlyList<string> SelectUnexpectedComponentKeys(
        TimberAnnotationMode mode,
        IReadOnlyList<TimberElementLabelCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }
        var required = RequiredRoles(mode);
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
                role == TimberMainAnnotationComponentRole.Primary,
            TimberAnnotationMode.DimensionsWithItemNumber =>
                role == TimberMainAnnotationComponentRole.FramedItem,
            TimberAnnotationMode.FullLabel =>
                role == TimberMainAnnotationComponentRole.Primary,
            _ => false,
        };
}
