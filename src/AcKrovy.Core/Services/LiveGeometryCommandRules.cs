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

    public static bool IsCopySourcePreservingCommand(string? globalCommandName)
    {
        if (string.IsNullOrWhiteSpace(globalCommandName))
        {
            return false;
        }

        var normalized = globalCommandName!.Trim().TrimStart('_', '.');
        return normalized.Equals("COPY", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("COPYCLIP", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("PASTECLIP", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresFullTimberAnnotationRefresh(string? globalCommandName)
    {
        if (string.IsNullOrWhiteSpace(globalCommandName))
        {
            return false;
        }

        var normalized = globalCommandName!.Trim().TrimStart('_', '.');
        return string.Equals(normalized, "ROTATE", StringComparison.OrdinalIgnoreCase);
    }
}
