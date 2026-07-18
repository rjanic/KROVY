using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabelMatchRules
{
    public static TimberElementLabelSelection SelectLabelForUpsert(
        string sourceHandle,
        string currentElementId,
        string? previousElementId,
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        int currentElementOwnerCount,
        int previousElementOwnerCount)
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
            var current = sourceMatches.FirstOrDefault(candidate =>
                HasSameValue(candidate.ElementId, currentElementId));
            var selected = current ?? sourceMatches[0];

            return new TimberElementLabelSelection
            {
                LabelKeyToUpdate = selected.LabelKey,
                LabelKeysToDelete = sourceMatches
                    .Where(candidate => !HasSameValue(candidate.LabelKey, selected.LabelKey))
                    .Select(candidate => candidate.LabelKey)
                    .ToList(),
            };
        }

        var currentFallback = SelectUniqueElementIdFallback(candidates, currentElementId, currentElementOwnerCount, 1);
        if (currentFallback is not null)
        {
            return new TimberElementLabelSelection { LabelKeyToUpdate = currentFallback.LabelKey };
        }

        if (!HasSameValue(previousElementId, currentElementId))
        {
            var previousFallback = SelectUniqueElementIdFallback(
                candidates,
                previousElementId,
                previousElementOwnerCount,
                0);
            if (previousFallback is not null)
            {
                return new TimberElementLabelSelection { LabelKeyToUpdate = previousFallback.LabelKey };
            }
        }

        return new TimberElementLabelSelection();
    }

    private static TimberElementLabelCandidate? SelectUniqueElementIdFallback(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        string? elementId,
        int ownerCount,
        int expectedOwnerCount)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var matches = candidates
            .Where(candidate => HasSameValue(candidate.ElementId, elementId))
            .ToList();

        return matches.Count == 1 && ownerCount == expectedOwnerCount ? matches[0] : null;
    }

    private static bool HasSameValue(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftValue = left!;
        var rightValue = right!;
        return string.Equals(leftValue.Trim(), rightValue.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
