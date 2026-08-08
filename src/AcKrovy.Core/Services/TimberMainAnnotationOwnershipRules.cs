using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// SourceHandle-first ownership guards for production main annotations.
/// ElementType/Material/Width/Height are content attributes and must never
/// authorize a second annotation for the same SourceHandle + logical role.
/// After type-change ElementId renumber (K→KL, K→VT), prefer the current
/// ElementId and drop superseded role duplicates / stale ElementId leftovers.
/// </summary>
public static class TimberMainAnnotationOwnershipRules
{
    public const int G5RendererGeneration = 5;

    /// <summary>
    /// When a SourceHandle already owns G4 Circle* components, any legacy
    /// <see cref="TimberMainAnnotationComponentRole.FramedItem"/> for that
    /// same handle is superseded and must be deleted — otherwise type-change
    /// refresh leaves a bound G4 composite plus an orphan framed leader.
    /// Production G5 Combined FramedItem (RendererGeneration=5) is never
    /// superseded by leftover G4 Circle* parts — those are deleted instead.
    /// Combined Plain Primary dimensions MText is intentionally kept when no G5.
    /// </summary>
    public static IReadOnlyList<string> SelectSupersededLegacyFramedLeaderKeys(
        IReadOnlyList<TimberElementLabelCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var handlesWithG4 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (IsG4CompositeRole(candidate.ComponentRole) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle))
            {
                handlesWithG4.Add(candidate.SourceHandle.Trim());
            }
        }

        if (handlesWithG4.Count == 0)
        {
            return Array.Empty<string>();
        }

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.LabelKey) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                handlesWithG4.Contains(candidate.SourceHandle.Trim()) &&
                candidate.ComponentRole ==
                    TimberMainAnnotationComponentRole.FramedItem &&
                !IsG5CombinedFramedItem(candidate))
            .Select(candidate => candidate.LabelKey)
            .ToArray();
    }

    /// <summary>
    /// When a SourceHandle already owns production G5 Combined
    /// (<see cref="TimberMainAnnotationComponentRole.FramedItem"/> +
    /// RendererGeneration=5), delete owned legacy G4 Circle* parts and the
    /// Combined Primary dimensions MText for that same handle.
    /// Foreign entities without our SourceHandle are never selected.
    /// </summary>
    public static IReadOnlyList<string> SelectLegacyCombinedPartsToDeleteWhenG5Present(
        IReadOnlyList<TimberElementLabelCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var handlesWithG5 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (IsG5CombinedFramedItem(candidate) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle))
            {
                handlesWithG5.Add(candidate.SourceHandle.Trim());
            }
        }

        if (handlesWithG5.Count == 0)
        {
            return Array.Empty<string>();
        }

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.LabelKey) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                handlesWithG5.Contains(candidate.SourceHandle.Trim()) &&
                (IsG4CompositeRole(candidate.ComponentRole) ||
                 candidate.ComponentRole ==
                     TimberMainAnnotationComponentRole.Primary))
            .Select(candidate => candidate.LabelKey)
            .ToArray();
    }

    public static bool IsG5CombinedFramedItem(
        TimberElementLabelCandidate candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        return candidate.ComponentRole ==
                TimberMainAnnotationComponentRole.FramedItem &&
            candidate.RendererGeneration == G5RendererGeneration;
    }

    /// <summary>
    /// Per SourceHandle keep at most one G4 AnnotationGroupId. Prefers groups
    /// whose ElementId matches <paramref name="preferredElementId"/>, then the
    /// largest group. Extra Circle* entities from other groups are deleted.
    /// </summary>
    public static IReadOnlyList<string> SelectExtraG4GroupKeysToDelete(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string? preferredElementId = null)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var keysToDelete = new List<string>();
        foreach (var sourceGroup in candidates
                     .Where(candidate =>
                         IsG4CompositeRole(candidate.ComponentRole) &&
                         !string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                         !string.IsNullOrWhiteSpace(candidate.LabelKey))
                     .GroupBy(
                         candidate => candidate.SourceHandle.Trim(),
                         StringComparer.OrdinalIgnoreCase))
        {
            var groupBuckets = sourceGroup
                .GroupBy(
                    candidate =>
                        string.IsNullOrWhiteSpace(candidate.AnnotationGroupId)
                            ? "\0ungrouped"
                            : candidate.AnnotationGroupId!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    GroupId = group.Key,
                    Keys = group.Select(candidate => candidate.LabelKey).ToList(),
                    PreferElementId = group.Count(candidate =>
                        HasSameValue(candidate.ElementId, preferredElementId)),
                    Count = group.Count(),
                })
                .OrderByDescending(group => group.PreferElementId)
                .ThenByDescending(group => group.Count)
                .ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groupBuckets.Count <= 1)
            {
                continue;
            }

            foreach (var extra in groupBuckets.Skip(1))
            {
                keysToDelete.AddRange(extra.Keys);
            }
        }

        return keysToDelete;
    }

    /// <summary>
    /// Per SourceHandle + ComponentRole keep a single annotation. Prefers
    /// <paramref name="preferredElementId"/> (type-change target), then a
    /// non-empty AnnotationGroupId, then stable LabelKey order.
    /// </summary>
    public static IReadOnlyList<string> SelectSurplusRoleKeysToDelete(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string? preferredElementId = null)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var keysToDelete = new List<string>();
        foreach (var roleGroup in candidates
                     .Where(candidate =>
                         !string.IsNullOrWhiteSpace(candidate.LabelKey) &&
                         !string.IsNullOrWhiteSpace(candidate.SourceHandle))
                     .GroupBy(
                         candidate =>
                             $"{candidate.SourceHandle.Trim()}|{candidate.ComponentRole}",
                         StringComparer.OrdinalIgnoreCase))
        {
            var ordered = roleGroup
                .OrderByDescending(candidate =>
                    HasSameValue(candidate.ElementId, preferredElementId))
                .ThenByDescending(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.AnnotationGroupId))
                .ThenBy(candidate => candidate.LabelKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ordered.Count <= 1)
            {
                continue;
            }

            keysToDelete.AddRange(ordered.Skip(1).Select(candidate => candidate.LabelKey));
        }

        return keysToDelete;
    }

    /// <summary>
    /// When a SourceHandle already owns at least one annotation for
    /// <paramref name="preferredElementId"/>, delete siblings on that handle
    /// whose ElementId still points at the previous type (K vs KL/VT).
    /// Call only after all Combined roles for the handle have been upserted —
    /// mid-flight G4 update still leaves Primary on the previous ElementId.
    /// </summary>
    public static IReadOnlyList<string> SelectStaleElementIdKeysToDelete(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string preferredElementId)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (string.IsNullOrWhiteSpace(preferredElementId))
        {
            return Array.Empty<string>();
        }

        var handlesWithPreferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                HasSameValue(candidate.ElementId, preferredElementId))
            {
                handlesWithPreferred.Add(candidate.SourceHandle.Trim());
            }
        }

        if (handlesWithPreferred.Count == 0)
        {
            return Array.Empty<string>();
        }

        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.LabelKey) &&
                !string.IsNullOrWhiteSpace(candidate.SourceHandle) &&
                handlesWithPreferred.Contains(candidate.SourceHandle.Trim()) &&
                !string.IsNullOrWhiteSpace(candidate.ElementId) &&
                !HasSameValue(candidate.ElementId, preferredElementId))
            .Select(candidate => candidate.LabelKey)
            .ToArray();
    }

    public static bool IsG4CompositeRole(TimberMainAnnotationComponentRole role) =>
        role is
            TimberMainAnnotationComponentRole.CircleText or
            TimberMainAnnotationComponentRole.CircleFrame or
            TimberMainAnnotationComponentRole.CircleLeaderLine;

    private static bool HasSameValue(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            left!.Trim(),
            right!.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
