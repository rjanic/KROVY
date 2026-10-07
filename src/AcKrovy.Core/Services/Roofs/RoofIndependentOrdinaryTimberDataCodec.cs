using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Stable, host-neutral schema for detached Ordinary plan and physical entities.</summary>
public static class RoofIndependentOrdinaryTimberDataCodec
{
    public static string Encode(RoofIndependentOrdinaryTimberData data)
    {
        if (!IsValid(data)) throw new ArgumentException("Invalid independent Ordinary data.", nameof(data));
        var key = data.SourceGeneratedMemberKey;
        return string.Join("|",
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            data.IndependentMemberId,
            data.OriginKind.ToString(),
            data.EntityRole.ToString(),
            data.SourceRoofReference ?? "-",
            key?.MemberKind.ToString() ?? "-",
            key?.RoofFace.ToString() ?? "-",
            key?.StationIndex.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    public static bool TryDecode(string? payload, out RoofIndependentOrdinaryTimberData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        var fields = payload!.Split('|');
        if (fields.Length != 8 ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var schema) ||
            !Enum.TryParse(fields[2], false, out RoofIndependentOrdinaryOriginKind origin) ||
            !Enum.TryParse(fields[3], false, out RoofIndependentOrdinaryEntityRole role)) return false;
        RoofGeneratedMemberKey? key = null;
        if (fields[5] != "-" || fields[6] != "-" || fields[7] != "-")
        {
            if (!Enum.TryParse(fields[5], false, out RoofGeneratedTimberKind kind) ||
                !Enum.TryParse(fields[6], false, out RafterRoofFace face) ||
                !int.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out var station))
                return false;
            key = new RoofGeneratedMemberKey(kind, face, station);
        }
        var candidate = new RoofIndependentOrdinaryTimberData(schema, fields[1], origin, role,
            fields[4] == "-" ? null : fields[4], key);
        if (!IsValid(candidate)) return false;
        data = candidate;
        return true;
    }

    public static bool IsValid(RoofIndependentOrdinaryTimberData? data) =>
        data is not null &&
        data.SchemaVersion == RoofIndependentOrdinaryTimberDataSchema.CurrentVersion &&
        Guid.TryParseExact(data.IndependentMemberId, "N", out _) &&
        Enum.IsDefined(typeof(RoofIndependentOrdinaryOriginKind), data.OriginKind) &&
        Enum.IsDefined(typeof(RoofIndependentOrdinaryEntityRole), data.EntityRole) &&
        (data.SourceRoofReference is null || IsSafe(data.SourceRoofReference)) &&
        (!data.SourceGeneratedMemberKey.HasValue ||
         (Enum.IsDefined(typeof(RoofGeneratedTimberKind), data.SourceGeneratedMemberKey.Value.MemberKind) &&
          Enum.IsDefined(typeof(RafterRoofFace), data.SourceGeneratedMemberKey.Value.RoofFace) &&
          data.SourceGeneratedMemberKey.Value.StationIndex >= 0));

    private static bool IsSafe(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOf('|') < 0;
}
