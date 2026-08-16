using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Guards generated-set replacement against ambiguous same-DWG COPY ownership.
/// Duplicate face/station pairs mean two physical sets collapsed onto one owner key.
/// </summary>
public static class RoofGeneratedTimberOwnershipRules
{
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
