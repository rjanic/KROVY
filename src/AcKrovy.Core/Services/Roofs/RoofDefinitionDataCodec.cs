using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Deterministic invariant payload codec. Field order is schema, kind, slope,
/// canonical ridge X/Y and the canonical footprint signature.
/// </summary>
public static class RoofDefinitionDataCodec
{
    private const char Separator = '|';
    private const string SimpleGableToken = "SimpleGable";
    private const double UnitDirectionTolerance = 0.000000001d;

    public static string Encode(RoofDefinitionData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (!TryValidate(data, out var error))
        {
            throw new ArgumentException($"Invalid roof definition data: {error}.", nameof(data));
        }

        return string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            SimpleGableToken,
            data.SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionX.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionY.ToString("R", CultureInfo.InvariantCulture),
            data.FootprintSignature);
    }

    public static bool TryDecode(
        string? payload,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var fields = payload!.Split(Separator);
        if (fields.Length == 0 || !int.TryParse(
                fields[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var schemaVersion))
        {
            return false;
        }

        if (schemaVersion > RoofDefinitionDataSchema.CurrentVersion)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedFutureSchema;
            return false;
        }

        if (schemaVersion != RoofDefinitionDataSchema.CurrentVersion || fields.Length != 6)
        {
            return false;
        }

        if (!string.Equals(fields[1], SimpleGableToken, StringComparison.Ordinal))
        {
            error = RoofDefinitionDataDecodeError.UnsupportedRoofKind;
            return false;
        }

        if (!TryParseFinite(fields[2], out var slope))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        if (!TryParseFinite(fields[3], out var directionX) ||
            !TryParseFinite(fields[4], out var directionY))
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeDirection;
            return false;
        }

        var candidate = new RoofDefinitionData(
            schemaVersion,
            RoofKind.SimpleGable,
            slope,
            directionX,
            directionY,
            fields[5]);
        if (!TryValidate(candidate, out error))
        {
            return false;
        }

        data = candidate;
        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    public static bool TryValidate(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (data.SchemaVersion != RoofDefinitionDataSchema.CurrentVersion)
        {
            error = data.SchemaVersion > RoofDefinitionDataSchema.CurrentVersion
                ? RoofDefinitionDataDecodeError.UnsupportedFutureSchema
                : RoofDefinitionDataDecodeError.MalformedPayload;
            return false;
        }

        if (data.Kind != RoofKind.SimpleGable)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedRoofKind;
            return false;
        }

        if (!IsFinite(data.SlopeDegrees) ||
            data.SlopeDegrees <= SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees ||
            data.SlopeDegrees >= SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees)
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        var directionLength = Math.Sqrt(
            data.RidgeDirectionX * data.RidgeDirectionX +
            data.RidgeDirectionY * data.RidgeDirectionY);
        if (!IsFinite(data.RidgeDirectionX) ||
            !IsFinite(data.RidgeDirectionY) ||
            !IsFinite(directionLength) ||
            Math.Abs(directionLength - 1d) > UnitDirectionTolerance ||
            data.RidgeDirectionX < 0d ||
            data.RidgeDirectionX == 0d && data.RidgeDirectionY < 0d)
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeDirection;
            return false;
        }

        if (!IsValidFootprintSignature(data.FootprintSignature))
        {
            error = RoofDefinitionDataDecodeError.InvalidFootprintSignature;
            return false;
        }

        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool IsValidFootprintSignature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Contains(Separator))
        {
            return false;
        }

        var vertices = signature!.Split(';');
        if (vertices.Length < 3)
        {
            return false;
        }

        foreach (var vertex in vertices)
        {
            var coordinates = vertex.Split(',');
            if (coordinates.Length != 2 ||
                !TryParseFinite(coordinates[0], out _) ||
                !TryParseFinite(coordinates[1], out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseFinite(string value, out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) && IsFinite(result);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
