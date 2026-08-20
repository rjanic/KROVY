namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Deterministic TRIM/BREAK fragment identity: pre-command snapshot handle remains
/// generated; appended handles become AttachedManual when both share a logical key.
/// </summary>
public static class RoofGeneratedMemberSplitIdentityRules
{
    public static bool TryResolveFragments(
        IReadOnlyList<string> liveMemberHandles,
        IReadOnlyCollection<string> snapshotGeneratedHandles,
        IReadOnlyCollection<string> appendedHandles,
        out string? generatedHandle,
        out IReadOnlyList<string> standaloneHandles)
    {
        generatedHandle = null;
        standaloneHandles = [];
        if (liveMemberHandles is null || liveMemberHandles.Count < 2)
        {
            return false;
        }

        var snapshotMatches = liveMemberHandles
            .Where(handle => RoofGeneratedMemberSplitRules.IsSnapshotGeneratedHandle(
                handle,
                snapshotGeneratedHandles))
            .ToArray();
        if (snapshotMatches.Length == 1)
        {
            generatedHandle = snapshotMatches[0];
            var keep = generatedHandle;
            standaloneHandles = liveMemberHandles
                .Where(handle => !string.Equals(handle, keep, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return true;
        }

        if (snapshotMatches.Length > 1)
        {
            return false;
        }

        var nonAppended = liveMemberHandles
            .Where(handle => !ContainsHandle(appendedHandles, handle))
            .ToArray();
        if (nonAppended.Length == 1)
        {
            generatedHandle = nonAppended[0];
            var keep = generatedHandle;
            standaloneHandles = liveMemberHandles
                .Where(handle => !string.Equals(handle, keep, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return true;
        }

        var appendedOnly = liveMemberHandles
            .Where(handle => ContainsHandle(appendedHandles, handle))
            .ToArray();
        if (appendedOnly.Length == liveMemberHandles.Count - 1 &&
            nonAppended.Length == 1)
        {
            generatedHandle = nonAppended[0];
            standaloneHandles = appendedOnly;
            return true;
        }

        return false;
    }

    private static bool ContainsHandle(IReadOnlyCollection<string> handles, string candidate)
    {
        if (handles is null || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        foreach (var handle in handles)
        {
            if (string.Equals(handle, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
