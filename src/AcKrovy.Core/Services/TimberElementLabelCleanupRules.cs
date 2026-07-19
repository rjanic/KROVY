using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabelCleanupRules
{
    public static IReadOnlyList<string> SelectLabelsWithoutExistingSourceHandleToDelete(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        IReadOnlyCollection<string> existingTimberSourceHandles)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (existingTimberSourceHandles is null)
        {
            throw new ArgumentNullException(nameof(existingTimberSourceHandles));
        }

        var existingHandles = CreateHandleSet(existingTimberSourceHandles);
        var keysToDelete = new List<string>();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.LabelKey) ||
                string.IsNullOrWhiteSpace(candidate.SourceHandle))
            {
                continue;
            }

            if (!existingHandles.Contains(candidate.SourceHandle.Trim()))
            {
                keysToDelete.Add(candidate.LabelKey);
            }
        }

        return keysToDelete;
    }

    public static IReadOnlyList<string> SelectDuplicateLabelKeysToDelete(
        IReadOnlyList<TimberElementLabelCandidate> candidates,
        IReadOnlyCollection<string> existingTimberSourceHandles)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (existingTimberSourceHandles is null)
        {
            throw new ArgumentNullException(nameof(existingTimberSourceHandles));
        }

        var existingHandles = CreateHandleSet(existingTimberSourceHandles);
        var seenSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keysToDelete = new List<string>();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.LabelKey) ||
                string.IsNullOrWhiteSpace(candidate.SourceHandle))
            {
                continue;
            }

            var sourceHandle = candidate.SourceHandle.Trim();
            if (!existingHandles.Contains(sourceHandle))
            {
                continue;
            }

            if (seenSourceHandles.Add(sourceHandle))
            {
                continue;
            }

            keysToDelete.Add(candidate.LabelKey);
        }

        return keysToDelete;
    }

    private static HashSet<string> CreateHandleSet(IReadOnlyCollection<string> sourceHandles) =>
        new(
            sourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);
}
