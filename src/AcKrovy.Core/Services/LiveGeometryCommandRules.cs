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

    public static bool IsCopySourcePreservingCommand(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        return normalized.Equals("COPY", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("COPYCLIP", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("PASTECLIP", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresFullTimberAnnotationRefresh(string? globalCommandName)
    {
        var normalized = NormalizeCommandName(globalCommandName);
        return string.Equals(normalized, "ROTATE", StringComparison.OrdinalIgnoreCase);
    }
}
