using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Guards generated-set replacement against ambiguous same-DWG COPY ownership.
/// Duplicate face/station pairs mean two physical sets collapsed onto one owner key.
/// </summary>
public static class RoofGeneratedTimberOwnershipRules
{
    /// <summary>
    /// Validates the integrity of an optional generated set for read-only state
    /// diagnostics. An empty set is consistent; a non-empty set is consistent only
    /// when every physical candidate has readable metadata and a unique logical key.
    /// </summary>
    public static bool IsConsistentOptionalSet(
        int generatedEntityCount,
        IReadOnlyList<RoofGeneratedMemberKey> readableMemberKeys)
    {
        if (generatedEntityCount < 0 ||
            readableMemberKeys is null ||
            generatedEntityCount != readableMemberKeys.Count)
        {
            return false;
        }

        return readableMemberKeys.Distinct().Count() == readableMemberKeys.Count;
    }

    public static bool HasUniqueMemberStations(IReadOnlyList<RoofGeneratedTimberData> members)
    {
        if (members is null || members.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<(RoofGeneratedTimberKind Kind, RafterRoofFace Face, int Station)>();
        foreach (var member in members)
        {
            if (member is null)
            {
                return false;
            }

            if (!seen.Add((member.MemberKind, member.RoofFace, member.StationIndex)))
            {
                return false;
            }
        }

        return true;
    }
}
