namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Portable erase policy for idempotent roof display rebuild.
/// Inspected children already scoped to this roof must be erased without
/// requiring the current owner-reference string to match (transferred / stale
/// JSON owner after COPY is a common host case).
/// </summary>
public static class RoofDisplayRebuildEraseRules
{
    /// <summary>
    /// Whether an inspected display child should be erased before creating the
    /// seven current Lines. Exists=true is enough; do not gate on owner string.
    /// </summary>
    public static bool ShouldEraseInspectedDisplayChild(bool displayStoreExists) =>
        displayStoreExists;

    /// <summary>
    /// Sweep orphan display Lines whose effective owner still equals the rebuild
    /// owner even when Inspect did not list them in ChildIds.
    /// </summary>
    public static bool ShouldEraseOwnerMatchedSweepChild(
        bool displayStoreExists,
        string? effectiveOwnerReference,
        string rebuildOwnerReference)
    {
        if (!displayStoreExists ||
            string.IsNullOrWhiteSpace(effectiveOwnerReference) ||
            string.IsNullOrWhiteSpace(rebuildOwnerReference))
        {
            return false;
        }

        return string.Equals(
            effectiveOwnerReference,
            rebuildOwnerReference,
            StringComparison.OrdinalIgnoreCase);
    }
}
