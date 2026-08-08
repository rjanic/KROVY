using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// SourceHandle-first G4 composite matching. ElementId fallback is allowed only
/// when a single timber owner exists and annotations do not span multiple
/// SourceHandles — mirroring <see cref="TimberElementLabelMatchRules"/>.
/// </summary>
public static class TimberFramedG4CompositeMatchRules
{
    public static TimberFramedG4CompositeSelection SelectCompositeForUpsert(
        string sourceHandle,
        string currentElementId,
        string? previousElementId,
        IReadOnlyList<TimberFramedG4CompositeCandidate> candidates,
        int currentElementOwnerCount,
        int previousElementOwnerCount,
        bool allowElementIdFallback = true)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var sourceMatches = candidates
            .Where(candidate => HasSameValue(candidate.SourceHandle, sourceHandle))
            .ToList();
        if (sourceMatches.Count > 0)
        {
            // Never mix another SourceHandle into this composite, even when
            // ElementId is shared across COPY instances.
            return SelectFromExclusiveOwnerSet(sourceMatches, currentElementId);
        }

        if (!allowElementIdFallback)
        {
            return TimberFramedG4CompositeSelection.Empty;
        }

        var currentFallback = SelectUniqueElementIdFallbackSet(
            candidates,
            currentElementId,
            currentElementOwnerCount,
            expectedOwnerCount: 1);
        if (currentFallback is not null)
        {
            return SelectFromExclusiveOwnerSet(currentFallback, currentElementId);
        }

        if (!HasSameValue(previousElementId, currentElementId))
        {
            var previousFallback = SelectUniqueElementIdFallbackSet(
                candidates,
                previousElementId,
                previousElementOwnerCount,
                expectedOwnerCount: 0);
            if (previousFallback is not null)
            {
                return SelectFromExclusiveOwnerSet(previousFallback, previousElementId);
            }
        }

        return TimberFramedG4CompositeSelection.Empty;
    }

    private static IReadOnlyList<TimberFramedG4CompositeCandidate>? SelectUniqueElementIdFallbackSet(
        IReadOnlyList<TimberFramedG4CompositeCandidate> candidates,
        string? elementId,
        int ownerCount,
        int expectedOwnerCount)
    {
        if (string.IsNullOrWhiteSpace(elementId) ||
            ownerCount != expectedOwnerCount)
        {
            return null;
        }

        var matches = candidates
            .Where(candidate => HasSameValue(candidate.ElementId, elementId))
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        // Ambiguous physical ownership — refuse rather than assemble a
        // Frankenstein composite across COPY siblings.
        var distinctSourceHandles = matches
            .Select(candidate => Normalize(candidate.SourceHandle))
            .Where(handle => handle.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctSourceHandles.Count > 1)
        {
            return null;
        }

        return matches;
    }

    private static TimberFramedG4CompositeSelection SelectFromExclusiveOwnerSet(
        IReadOnlyList<TimberFramedG4CompositeCandidate> exclusiveOwnerCandidates,
        string? preferredElementId)
    {
        var preferredGroupId = ResolvePreferredAnnotationGroupId(
            exclusiveOwnerCandidates,
            preferredElementId);

        string? leaderKey = null;
        string? frameKey = null;
        string? itemCodeKey = null;
        string? legacyKey = null;
        var keysToDelete = new List<string>();

        foreach (var candidate in OrderCandidates(
                     exclusiveOwnerCandidates,
                     preferredElementId,
                     preferredGroupId))
        {
            if (string.IsNullOrWhiteSpace(candidate.EntityKey))
            {
                continue;
            }

            if (candidate.IsLegacyBlockLeader)
            {
                AssignOrDelete(ref legacyKey, candidate.EntityKey, keysToDelete);
                continue;
            }

            switch (candidate.ComponentRole)
            {
                case TimberMainAnnotationComponentRole.CircleLeaderLine:
                    AssignOrDelete(ref leaderKey, candidate.EntityKey, keysToDelete);
                    break;
                case TimberMainAnnotationComponentRole.CircleFrame:
                    AssignOrDelete(ref frameKey, candidate.EntityKey, keysToDelete);
                    break;
                case TimberMainAnnotationComponentRole.CircleText:
                    AssignOrDelete(ref itemCodeKey, candidate.EntityKey, keysToDelete);
                    break;
            }
        }

        return new TimberFramedG4CompositeSelection
        {
            LeaderKey = leaderKey,
            FrameKey = frameKey,
            ItemCodeKey = itemCodeKey,
            LegacyBlockLeaderKey = legacyKey,
            AnnotationGroupId = preferredGroupId,
            EntityKeysToDelete = keysToDelete,
        };
    }

    private static IEnumerable<TimberFramedG4CompositeCandidate> OrderCandidates(
        IReadOnlyList<TimberFramedG4CompositeCandidate> candidates,
        string? preferredElementId,
        string? preferredGroupId) =>
        candidates
            .OrderByDescending(candidate =>
                HasSameValue(candidate.ElementId, preferredElementId))
            .ThenByDescending(candidate =>
                HasSameValue(candidate.AnnotationGroupId, preferredGroupId))
            .ThenBy(candidate => candidate.EntityKey, StringComparer.OrdinalIgnoreCase);

    private static string? ResolvePreferredAnnotationGroupId(
        IReadOnlyList<TimberFramedG4CompositeCandidate> candidates,
        string? preferredElementId)
    {
        var preferred = candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.AnnotationGroupId) &&
                HasSameValue(candidate.ElementId, preferredElementId))
            .Select(candidate => candidate.AnnotationGroupId!.Trim())
            .GroupBy(groupId => groupId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.AnnotationGroupId))
            .Select(candidate => candidate.AnnotationGroupId!.Trim())
            .GroupBy(groupId => groupId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static void AssignOrDelete(
        ref string? selectedKey,
        string entityKey,
        List<string> keysToDelete)
    {
        if (selectedKey is null)
        {
            selectedKey = entityKey;
            return;
        }

        if (!HasSameValue(selectedKey, entityKey))
        {
            keysToDelete.Add(entityKey);
        }
    }

    private static bool HasSameValue(string? left, string? right)
    {
        var leftValue = Normalize(left);
        var rightValue = Normalize(right);
        if (leftValue.Length == 0 || rightValue.Length == 0)
        {
            return false;
        }

        return string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
}
