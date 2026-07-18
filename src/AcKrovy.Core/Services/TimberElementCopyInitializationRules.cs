using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementCopyInitializationRules
{
    public static bool ShouldInitializeAsNewPhysicalElement(
        string currentSourceHandle,
        string elementId,
        IReadOnlyList<TimberElementLabelCandidate> labelCandidates,
        IReadOnlyCollection<string> existingTimberSourceHandles)
    {
        if (string.IsNullOrWhiteSpace(currentSourceHandle) ||
            string.IsNullOrWhiteSpace(elementId))
        {
            return false;
        }

        if (labelCandidates is null)
        {
            throw new ArgumentNullException(nameof(labelCandidates));
        }

        if (existingTimberSourceHandles is null)
        {
            throw new ArgumentNullException(nameof(existingTimberSourceHandles));
        }

        var existingHandles = new HashSet<string>(
            existingTimberSourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (existingHandles.Count == 0)
        {
            return false;
        }

        var hasCurrentSourceLabel = labelCandidates.Any(candidate =>
            HasSameValue(candidate.SourceHandle, currentSourceHandle));
        if (hasCurrentSourceLabel)
        {
            return false;
        }

        return labelCandidates.Any(candidate =>
            HasSameValue(candidate.ElementId, elementId) &&
            !HasSameValue(candidate.SourceHandle, currentSourceHandle) &&
            existingHandles.Contains(candidate.SourceHandle.Trim()));
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
