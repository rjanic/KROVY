using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Strict structural erase eligibility for derived roof display Lines that remain
/// grouped with a semantic source after native GROUP COPY, even when JSON or clone
/// handle owner references are stale. Used only for erase — never for semantic adoption.
/// </summary>
public static class RoofDisplayForeignGroupEraseRules
{
    private static readonly RoofDisplayEdgeRole[] RequiredRoles =
    [
        RoofDisplayEdgeRole.Ridge,
        RoofDisplayEdgeRole.Eave0,
        RoofDisplayEdgeRole.Eave1,
        RoofDisplayEdgeRole.GableSlope00,
        RoofDisplayEdgeRole.GableSlope01,
        RoofDisplayEdgeRole.GableSlope10,
        RoofDisplayEdgeRole.GableSlope11,
    ];

    /// <summary>
    /// Selects the seven display Line member keys from one GROUP when the strict
    /// source + 7-role roof topology contract is fully satisfied.
    /// </summary>
    public static bool TrySelectDisplayEraseMemberKeys(
        bool sourceHasValidRoofDefinition,
        IReadOnlyList<RoofDisplayForeignGroupMemberObservation> members,
        out IReadOnlyList<string> eraseMemberKeys)
    {
        eraseMemberKeys = Array.Empty<string>();
        if (!sourceHasValidRoofDefinition || members is null)
        {
            return false;
        }

        if (members.Count != SimpleGableRoofWireframe.EdgeCount + 1)
        {
            return false;
        }

        var sourceCount = 0;
        var displayKeys = new List<string>(SimpleGableRoofWireframe.EdgeCount);
        var roles = new HashSet<RoofDisplayEdgeRole>();
        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.MemberKey))
            {
                return false;
            }

            switch (member.Kind)
            {
                case RoofDisplayForeignGroupMemberKind.SourcePolyline:
                    sourceCount++;
                    break;
                case RoofDisplayForeignGroupMemberKind.DisplayLine:
                    if (!member.HasReadableRoofDisplayMetadata ||
                        !member.SchemaSupported ||
                        member.Role is not { } role ||
                        !roles.Add(role))
                    {
                        return false;
                    }

                    displayKeys.Add(member.MemberKey);
                    break;
                case RoofDisplayForeignGroupMemberKind.Other:
                    return false;
                default:
                    return false;
            }
        }

        if (sourceCount != 1 ||
            displayKeys.Count != SimpleGableRoofWireframe.EdgeCount ||
            roles.Count != SimpleGableRoofWireframe.EdgeCount)
        {
            return false;
        }

        foreach (var required in RequiredRoles)
        {
            if (!roles.Contains(required))
            {
                return false;
            }
        }

        if (displayKeys.Distinct(StringComparer.Ordinal).Count() != displayKeys.Count)
        {
            return false;
        }

        eraseMemberKeys = displayKeys;
        return true;
    }

    /// <summary>
    /// Across multiple matching foreign groups, accept erase keys only when the
    /// candidate set is unique (identical). Ambiguous differing sets are rejected.
    /// </summary>
    public static bool TryResolveUniqueEraseMemberKeys(
        IReadOnlyList<IReadOnlyList<string>> candidateSets,
        out IReadOnlyList<string> eraseMemberKeys)
    {
        eraseMemberKeys = Array.Empty<string>();
        if (candidateSets is null || candidateSets.Count == 0)
        {
            return false;
        }

        IReadOnlyList<string>? accepted = null;
        foreach (var candidate in candidateSets)
        {
            if (candidate is null ||
                candidate.Count != SimpleGableRoofWireframe.EdgeCount ||
                candidate.Distinct(StringComparer.Ordinal).Count() != candidate.Count)
            {
                return false;
            }

            if (accepted is null)
            {
                accepted = candidate;
                continue;
            }

            if (!SetEquals(accepted, candidate))
            {
                return false;
            }
        }

        eraseMemberKeys = accepted ?? Array.Empty<string>();
        return eraseMemberKeys.Count == SimpleGableRoofWireframe.EdgeCount;
    }

    /// <summary>
    /// Unions erase candidate keys and deduplicates. Stale owner / 1005 never gate
    /// membership once a strict foreign-group set was accepted.
    /// </summary>
    public static IReadOnlyList<string> UnionDeduplicateEraseMemberKeys(
        IReadOnlyList<string> inspectedKeys,
        IReadOnlyList<string> ownerMatchedKeys,
        IReadOnlyList<string> foreignGroupKeys)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        AddRange(set, inspectedKeys);
        AddRange(set, ownerMatchedKeys);
        AddRange(set, foreignGroupKeys);
        return set.ToList();
    }

    private static void AddRange(HashSet<string> set, IReadOnlyList<string>? keys)
    {
        if (keys is null)
        {
            return;
        }

        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                set.Add(key);
            }
        }
    }

    private static bool SetEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var set = new HashSet<string>(left, StringComparer.Ordinal);
        return set.SetEquals(right);
    }
}
