using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Invariant schema-1 codec for generated-timber ownership metadata.</summary>
public static class RoofGeneratedTimberDataCodec
{
    private const char Separator = '|';

    public static string Encode(RoofGeneratedTimberData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!TryValidate(data, out var error))
        {
            throw new ArgumentException($"Invalid roof-generated timber data: {error}.", nameof(data));
        }

        return string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            data.RoofOwnerReference,
            data.MemberKind.ToString(),
            data.RoofFace.ToString(),
            data.StationIndex.ToString(CultureInfo.InvariantCulture),
            data.StationCount.ToString(CultureInfo.InvariantCulture),
            data.RequestedMaximumSpacingMm.ToString("R", CultureInfo.InvariantCulture),
            data.LayoutSignature);
    }

    public static bool TryDecode(
        string? payload,
        out RoofGeneratedTimberData? data,
        out RoofGeneratedTimberDataDecodeError error)
    {
        data = null;
        error = RoofGeneratedTimberDataDecodeError.MalformedPayload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length != 8 ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var schema))
        {
            return false;
        }
        if (schema > RoofGeneratedTimberDataSchema.CurrentVersion)
        {
            error = RoofGeneratedTimberDataDecodeError.UnsupportedFutureSchema;
            return false;
        }
        if (!Enum.TryParse(fields[2], false, out RoofGeneratedTimberKind memberKind) ||
            !Enum.IsDefined(typeof(RoofGeneratedTimberKind), memberKind))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidMemberKind;
            return false;
        }
        if (!Enum.TryParse(fields[3], false, out RafterRoofFace roofFace) ||
            !Enum.IsDefined(typeof(RafterRoofFace), roofFace))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidRoofFace;
            return false;
        }
        if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var stationIndex) ||
            !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var stationCount) ||
            !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing))
        {
            return false;
        }

        var candidate = new RoofGeneratedTimberData(
            schema,
            fields[1],
            memberKind,
            roofFace,
            stationIndex,
            stationCount,
            spacing,
            fields[7]);
        if (!TryValidate(candidate, out error))
        {
            return false;
        }

        data = candidate;
        error = RoofGeneratedTimberDataDecodeError.None;
        return true;
    }

    public static bool TryValidate(
        RoofGeneratedTimberData data,
        out RoofGeneratedTimberDataDecodeError error)
    {
        if (data.SchemaVersion > RoofGeneratedTimberDataSchema.CurrentVersion)
        {
            error = RoofGeneratedTimberDataDecodeError.UnsupportedFutureSchema;
            return false;
        }
        if (data.SchemaVersion != RoofGeneratedTimberDataSchema.CurrentVersion)
        {
            error = RoofGeneratedTimberDataDecodeError.MalformedPayload;
            return false;
        }
        if (!IsSafeText(data.RoofOwnerReference))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidOwnerReference;
            return false;
        }
        if (!Enum.IsDefined(typeof(RoofGeneratedTimberKind), data.MemberKind))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidMemberKind;
            return false;
        }
        if (!Enum.IsDefined(typeof(RafterRoofFace), data.RoofFace))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidRoofFace;
            return false;
        }
        if (data.StationCount < 2 ||
            data.StationIndex < 0 ||
            data.StationIndex >= data.StationCount)
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidStation;
            return false;
        }
        if (double.IsNaN(data.RequestedMaximumSpacingMm) ||
            double.IsInfinity(data.RequestedMaximumSpacingMm) ||
            data.RequestedMaximumSpacingMm <= 0d)
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidMaximumSpacing;
            return false;
        }
        if (!IsSafeText(data.LayoutSignature))
        {
            error = RoofGeneratedTimberDataDecodeError.InvalidLayoutSignature;
            return false;
        }

        error = RoofGeneratedTimberDataDecodeError.None;
        return true;
    }

    private static bool IsSafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value!.IndexOf(Separator) < 0;
}
