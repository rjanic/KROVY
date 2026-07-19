using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementLabelCleanupRules
{
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

        var existingHandles = new HashSet<string>(
            existingTimberSourceHandles
                .Where(handle => !string.IsNullOrWhiteSpace(handle))
                .Select(handle => handle.Trim()),
            StringComparer.OrdinalIgnoreCase);
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
}
