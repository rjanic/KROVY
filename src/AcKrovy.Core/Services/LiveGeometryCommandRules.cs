namespace AcKrovy.Core.Services;

public static class LiveGeometryCommandRules
{
    public static IReadOnlyList<T> SelectIncrementalCandidates<T>(
        bool preserveCopySources,
        IEnumerable<T> modifiedIds,
        IEnumerable<T> appendedIds) =>
        (preserveCopySources ? appendedIds : modifiedIds)
        .Distinct()
        .ToArray();

    /// <summary>
    /// Normalizes AutoCAD Managed API GlobalCommandName values for comparison:
    /// trim whitespace and transparent/international prefixes ('_', '.', '\'').
    /// </summary>
    public static string NormalizeCommandName(string? globalCommandName)
    {
        if (string.IsNullOrWhiteSpace(globalCommandName))
        {
            return string.Empty;
        }

        return globalCommandName!.Trim().TrimStart('_', '.', '\'');
    }

    /// <summary>
    /// Native Undo/Redo family. Live geometry must not open LockDocument /
    /// StartTransaction after these — even an empty Commit clears the REDO stack.
    /// Exact match after <see cref="NormalizeCommandName"/> only (does not
    /// swallow STRETCH/MOVE/ROTATE/TRIM/EXTEND/COPY or grip edits).
    /// </summary>
    public static bool IsUndoRedoCommand(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Equals("U", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("UNDO", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("REDO", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("MREDO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Native source edits whose CommandEnded plugin writes must share one undo
    /// group with the host command. Exact match after
    /// <see cref="NormalizeCommandName"/> only.
    /// </summary>
    public static bool IsUndoGroupingSourceCommand(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        return normalized.Equals("STRETCH", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("GRIP_STRETCH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCopySourcePreservingCommand(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        return normalized.Equals("COPY", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("COPYCLIP", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("PASTECLIP", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Historical ROTATE full-refresh flag (rotate-aware labels, Jul 2026).
    /// Kept as a <em>fallback</em> when ObjectModified captured no timber sources.
    /// When modified timber identifiers are available, prefer
    /// <see cref="SelectSourceRefreshCandidates{T}"/> incremental refresh.
    /// </summary>
    public static bool RequiresFullTimberAnnotationRefresh(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        return string.Equals(normalized, "ROTATE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Chooses which timber sources receive EnsureForElement after a live edit.
    /// Prefer the ObjectModified timber set (1 rotated source → 1 refresh, N → N).
    /// Full ModelSpace FindAll is only the ROTATE fallback when no timber mods
    /// were observed.
    /// </summary>
    public static IReadOnlyList<T> SelectSourceRefreshCandidates<T>(
        bool preserveCopySources,
        bool requiresFullTimberAnnotationRefresh,
        IReadOnlyList<T> modifiedIds,
        IReadOnlyList<T> appendedIds,
        IReadOnlyList<T> modifiedTimberIds,
        Func<IReadOnlyList<T>> findAllTimberElements)
    {
        if (preserveCopySources)
        {
            return SelectIncrementalCandidates(true, modifiedIds, appendedIds);
        }

        if (modifiedTimberIds.Count > 0)
        {
            return modifiedTimberIds.Distinct().ToArray();
        }

        if (requiresFullTimberAnnotationRefresh)
        {
            return findAllTimberElements() ?? Array.Empty<T>();
        }

        return SelectIncrementalCandidates(false, modifiedIds, appendedIds);
    }
}
