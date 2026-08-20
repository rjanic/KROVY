using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public static class RoofAttachedManualTimberDataCodec
{
    private const char Separator = '|';

    public static string Encode(RoofAttachedManualTimberData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!TryValidate(data, out _))
        {
            throw new ArgumentException("Invalid attached-manual timber data.", nameof(data));
        }

        if (data.SchemaVersion >= 2 &&
            data.AnchorGeneratedMemberKey is not null &&
            data.RelativeSegment is not null)
        {
            var key = data.AnchorGeneratedMemberKey.Value;
            var rel = data.RelativeSegment;
            var fields = new List<string>
            {
                data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                data.RoofOwnerReference,
                data.ChildIdentity,
                data.Role.ToString(),
                key.MemberKind.ToString(),
                key.RoofFace.ToString(),
                key.StationIndex.ToString(CultureInfo.InvariantCulture),
                rel.U0Mm.ToString("R", CultureInfo.InvariantCulture),
                rel.V0Mm.ToString("R", CultureInfo.InvariantCulture),
                rel.W0Mm.ToString("R", CultureInfo.InvariantCulture),
                rel.U1Mm.ToString("R", CultureInfo.InvariantCulture),
                rel.V1Mm.ToString("R", CultureInfo.InvariantCulture),
                rel.W1Mm.ToString("R", CultureInfo.InvariantCulture),
            };
            if (data.SchemaVersion >= 3)
            {
                fields.Add(data.Origin.ToString());
            }

            return string.Join(Separator.ToString(), fields);
        }

        return string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            data.RoofOwnerReference,
            data.ChildIdentity,
            data.Role.ToString());
    }

    public static bool TryDecode(string? payload, out RoofAttachedManualTimberData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length < 4 ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var schema) ||
            schema < 1 ||
            schema > RoofAttachedManualTimberDataSchema.CurrentVersion)
        {
            return false;
        }

        if (!Enum.TryParse(fields[3], false, out RoofTimberChildRole role) ||
            !Enum.IsDefined(typeof(RoofTimberChildRole), role))
        {
            return false;
        }

        if (schema >= 2 && fields.Length >= 13 &&
            Enum.TryParse(fields[4], false, out RoofGeneratedTimberKind kind) &&
            Enum.IsDefined(typeof(RoofGeneratedTimberKind), kind) &&
            Enum.TryParse(fields[5], false, out RafterRoofFace face) &&
            Enum.IsDefined(typeof(RafterRoofFace), face) &&
            int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var station) &&
            TryParseDouble(fields[7], out var u0) &&
            TryParseDouble(fields[8], out var v0) &&
            TryParseDouble(fields[9], out var w0) &&
            TryParseDouble(fields[10], out var u1) &&
            TryParseDouble(fields[11], out var v1) &&
            TryParseDouble(fields[12], out var w1))
        {
            var origin = RoofAttachedManualOrigin.Split;
            if (schema >= 3)
            {
                if (fields.Length < 14 ||
                    !Enum.TryParse(fields[13], false, out origin) ||
                    !Enum.IsDefined(typeof(RoofAttachedManualOrigin), origin))
                {
                    return false;
                }
            }

            data = new RoofAttachedManualTimberData(
                schema,
                fields[1],
                fields[2],
                role,
                new RoofGeneratedMemberKey(kind, face, station),
                new RoofAttachedManualRelativeSegment(u0, v0, w0, u1, v1, w1),
                origin);
            return TryValidate(data, out _);
        }

        data = new RoofAttachedManualTimberData(schema, fields[1], fields[2], role);
        return TryValidate(data, out _);
    }

    public static bool TryValidate(RoofAttachedManualTimberData data, out string error)
    {
        error = string.Empty;
        if (data is null ||
            data.SchemaVersion < 1 ||
            data.SchemaVersion > RoofAttachedManualTimberDataSchema.CurrentVersion ||
            string.IsNullOrWhiteSpace(data.RoofOwnerReference) ||
            string.IsNullOrWhiteSpace(data.ChildIdentity) ||
            data.Role != RoofTimberChildRole.AttachedManual)
        {
            error = "invalid-attached-manual";
            return false;
        }

        if (data.SchemaVersion >= 2)
        {
            if (data.AnchorGeneratedMemberKey is null || data.RelativeSegment is null)
            {
                error = "invalid-attached-manual-v2";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
