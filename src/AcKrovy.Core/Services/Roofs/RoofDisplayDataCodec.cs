using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Deterministic invariant codec for generated display-child ownership.</summary>
public static class RoofDisplayDataCodec
{
    private const char Separator = '|';

    public static string Encode(RoofDisplayData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!TryValidate(data, out var error))
        {
            throw new ArgumentException($"Invalid roof display data: {error}.", nameof(data));
        }

        return string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            data.OwnerReference,
            data.Role.ToString(),
            data.GenerationSignature);
    }

    public static bool TryDecode(
        string? payload,
        out RoofDisplayData? data,
        out RoofDisplayDataDecodeError error)
    {
        data = null;
        error = RoofDisplayDataDecodeError.MalformedPayload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length != 4 || !int.TryParse(
                fields[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var schema))
        {
            return false;
        }
        if (schema > RoofDisplayDataSchema.CurrentVersion)
        {
            error = RoofDisplayDataDecodeError.UnsupportedFutureSchema;
            return false;
        }
        if (!Enum.TryParse(fields[2], false, out RoofDisplayEdgeRole role) ||
            !Enum.IsDefined(typeof(RoofDisplayEdgeRole), role))
        {
            error = RoofDisplayDataDecodeError.InvalidRole;
            return false;
        }

        var candidate = new RoofDisplayData(schema, fields[1], role, fields[3]);
        if (!TryValidate(candidate, out error))
        {
            return false;
        }

        data = candidate;
        error = RoofDisplayDataDecodeError.None;
        return true;
    }

    public static bool TryReadOwnerReference(string? payload, out string? ownerReference)
    {
        ownerReference = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length < 2 || !IsSafeText(fields[1]))
        {
            return false;
        }

        ownerReference = fields[1];
        return true;
    }

    public static bool TryValidate(
        RoofDisplayData data,
        out RoofDisplayDataDecodeError error)
    {
        if (data.SchemaVersion > RoofDisplayDataSchema.CurrentVersion)
        {
            error = RoofDisplayDataDecodeError.UnsupportedFutureSchema;
            return false;
        }
        if (data.SchemaVersion != RoofDisplayDataSchema.CurrentVersion)
        {
            error = RoofDisplayDataDecodeError.MalformedPayload;
            return false;
        }
        if (!IsSafeText(data.OwnerReference))
        {
            error = RoofDisplayDataDecodeError.InvalidOwnerReference;
            return false;
        }
        if (!Enum.IsDefined(typeof(RoofDisplayEdgeRole), data.Role))
        {
            error = RoofDisplayDataDecodeError.InvalidRole;
            return false;
        }
        if (!IsSafeText(data.GenerationSignature))
        {
            error = RoofDisplayDataDecodeError.InvalidGenerationSignature;
            return false;
        }

        error = RoofDisplayDataDecodeError.None;
        return true;
    }

    private static bool IsSafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value!.IndexOf(Separator) < 0;
}
