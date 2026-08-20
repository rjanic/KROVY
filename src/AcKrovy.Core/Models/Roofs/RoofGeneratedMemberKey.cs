namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Stable logical identity of one roof-generated timber member.
/// Reuses the generator's deterministic face/station ordinal — not a CAD handle.
/// </summary>
public readonly record struct RoofGeneratedMemberKey(
    RoofGeneratedTimberKind MemberKind,
    RafterRoofFace RoofFace,
    int StationIndex)
{
    public static RoofGeneratedMemberKey From(RoofGeneratedTimberData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return new(data.MemberKind, data.RoofFace, data.StationIndex);
    }

    public static RoofGeneratedMemberKey From(SimpleGableRafter rafter)
    {
        if (rafter is null)
        {
            throw new ArgumentNullException(nameof(rafter));
        }

        return new(RoofGeneratedTimberKind.Rafter, rafter.Face, rafter.StationIndex);
    }

    public bool MapsToCurrentLayout(int stationCount) =>
        StationIndex >= 0 && stationCount >= 2 && StationIndex < stationCount;
}
