using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Same-DWG COPY detach: pre-command generated handles stay on the parent roof;
/// appended handles that duplicate an existing logical key on that owner detach.
/// </summary>
public static class RoofGeneratedRafterCopyDetachRules
{
    public sealed record AppendedGeneratedLine(
        string Handle,
        string OwnerReference,
        RafterRoofFace Face,
        int StationIndex);

    public static string FormatLogicalKey(RafterRoofFace face, int stationIndex) =>
        $"{face}:s{stationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static IReadOnlyList<string> FindAppendedCloneDetachHandles(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> preCommandLogicalKeysByOwner,
        IReadOnlyCollection<string> preCommandGeneratedHandles,
        IReadOnlyList<AppendedGeneratedLine> appendedLines,
        IReadOnlyCollection<string> wholeRoofRewriteMemberHandles)
    {
        if (preCommandLogicalKeysByOwner is null ||
            preCommandLogicalKeysByOwner.Count == 0 ||
            preCommandGeneratedHandles is null ||
            preCommandGeneratedHandles.Count == 0 ||
            appendedLines is null ||
            appendedLines.Count == 0)
        {
            return [];
        }

        var rewrite = wholeRoofRewriteMemberHandles is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(wholeRoofRewriteMemberHandles, StringComparer.OrdinalIgnoreCase);
        var preHandles = new HashSet<string>(preCommandGeneratedHandles, StringComparer.OrdinalIgnoreCase);
        var detach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in appendedLines)
        {
            if (line is null ||
                string.IsNullOrWhiteSpace(line.Handle) ||
                string.IsNullOrWhiteSpace(line.OwnerReference) ||
                preHandles.Contains(line.Handle) ||
                rewrite.Contains(line.Handle))
            {
                continue;
            }

            if (!preCommandLogicalKeysByOwner.TryGetValue(
                    line.OwnerReference,
                    out var preKeys) ||
                preKeys is null ||
                preKeys.Count == 0)
            {
                continue;
            }

            var logicalKey = FormatLogicalKey(line.Face, line.StationIndex);
            if (preKeys.Any(key => string.Equals(key, logicalKey, StringComparison.OrdinalIgnoreCase)))
            {
                detach.Add(line.Handle);
            }
        }

        return detach.ToArray();
    }
}
