using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Deterministic invariant codec for legacy schema 1 and topology-relative schema 2.</summary>
public static class RoofDefinitionDataCodec
{
    private const char Separator = '|';
    private const string SimpleGableToken = "SimpleGable";
    private const string Edge01Token = "Edge01";
    private const string Edge12Token = "Edge12";
    private const string ClockwiseToken = "CW";
    private const string CounterClockwiseToken = "CCW";
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

        return data.SchemaVersion switch
        {
            RoofDefinitionDataSchema.LegacyAbsoluteVersion => EncodeV1(data),
            RoofDefinitionDataSchema.CurrentVersion => EncodeV2(data),
            _ => throw new ArgumentException("Unsupported roof schema.", nameof(data)),
        };
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

        return schemaVersion switch
        {
            RoofDefinitionDataSchema.LegacyAbsoluteVersion =>
                TryDecodeV1(fields, out data, out error),
            RoofDefinitionDataSchema.CurrentVersion =>
                TryDecodeV2(fields, out data, out error),
            _ => false,
        };
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
        if (data.SchemaVersion > RoofDefinitionDataSchema.CurrentVersion)
        {
            error = RoofDefinitionDataDecodeError.UnsupportedFutureSchema;
            return false;
        }

        if (data.SchemaVersion is not (
                RoofDefinitionDataSchema.LegacyAbsoluteVersion or
                RoofDefinitionDataSchema.CurrentVersion))
        {
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

        return data.SchemaVersion == RoofDefinitionDataSchema.LegacyAbsoluteVersion
            ? TryValidateV1(data, out error)
            : TryValidateV2(data, out error);
    }

    private static string EncodeV1(RoofDefinitionData data) =>
        string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            SimpleGableToken,
            data.SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionX!.Value.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeDirectionY!.Value.ToString("R", CultureInfo.InvariantCulture),
            data.FootprintSignature);

    private static string EncodeV2(RoofDefinitionData data)
    {
        var descriptor = data.RigidFootprint!;
        return string.Join(
            Separator.ToString(),
            data.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            SimpleGableToken,
            data.SlopeDegrees.ToString("R", CultureInfo.InvariantCulture),
            data.RidgeEdgeFamily == RoofRidgeEdgeFamily.SourceEdge01
                ? Edge01Token
                : Edge12Token,
            descriptor.VertexCount.ToString(CultureInfo.InvariantCulture),
            descriptor.SourceOrientation == RoofPolygonOrientation.Clockwise
                ? ClockwiseToken
                : CounterClockwiseToken,
            descriptor.Edge01LengthMm.ToString("R", CultureInfo.InvariantCulture),
            descriptor.Edge12LengthMm.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool TryDecodeV1(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count != 6 || !TryReadKind(fields[1], out error))
        {
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
            RoofDefinitionDataSchema.LegacyAbsoluteVersion,
            RoofKind.SimpleGable,
            slope,
            directionX,
            directionY,
            fields[5]);
        return Accept(candidate, out data, out error);
    }

    private static bool TryDecodeV2(
        IReadOnlyList<string> fields,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        data = null;
        error = RoofDefinitionDataDecodeError.MalformedPayload;
        if (fields.Count != 8 || !TryReadKind(fields[1], out error))
        {
            return false;
        }

        if (!TryParseFinite(fields[2], out var slope))
        {
            error = RoofDefinitionDataDecodeError.InvalidSlope;
            return false;
        }

        var edgeFamily = fields[3] switch
        {
            Edge01Token => RoofRidgeEdgeFamily.SourceEdge01,
            Edge12Token => RoofRidgeEdgeFamily.SourceEdge12,
            _ => RoofRidgeEdgeFamily.Undefined,
        };
        if (edgeFamily == RoofRidgeEdgeFamily.Undefined)
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily;
            return false;
        }

        if (!int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            !TryReadOrientation(fields[5], out var orientation) ||
            !TryParseFinite(fields[6], out var edge01Length) ||
            !TryParseFinite(fields[7], out var edge12Length))
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        var candidate = new RoofDefinitionData(
            RoofDefinitionDataSchema.CurrentVersion,
            RoofKind.SimpleGable,
            slope,
            RidgeEdgeFamily: edgeFamily,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                count,
                orientation,
                edge01Length,
                edge12Length));
        return Accept(candidate, out data, out error);
    }

    private static bool TryValidateV1(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data.RidgeEdgeFamily is not null || data.RigidFootprint is not null)
        {
            error = RoofDefinitionDataDecodeError.MalformedPayload;
            return false;
        }

        var x = data.RidgeDirectionX;
        var y = data.RidgeDirectionY;
        var directionLength = x is not null && y is not null
            ? Math.Sqrt(x.Value * x.Value + y.Value * y.Value)
            : double.NaN;
        if (x is null || y is null ||
            !IsFinite(x.Value) || !IsFinite(y.Value) || !IsFinite(directionLength) ||
            Math.Abs(directionLength - 1d) > UnitDirectionTolerance ||
            x.Value < 0d || x.Value == 0d && y.Value < 0d)
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

    private static bool TryValidateV2(
        RoofDefinitionData data,
        out RoofDefinitionDataDecodeError error)
    {
        if (data.RidgeDirectionX is not null ||
            data.RidgeDirectionY is not null ||
            data.FootprintSignature is not null)
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        if (data.RidgeEdgeFamily is not (
                RoofRidgeEdgeFamily.SourceEdge01 or RoofRidgeEdgeFamily.SourceEdge12))
        {
            error = RoofDefinitionDataDecodeError.InvalidRidgeEdgeFamily;
            return false;
        }

        var descriptor = data.RigidFootprint;
        if (descriptor is null || descriptor.VertexCount != 4 ||
            descriptor.SourceOrientation is not (
                RoofPolygonOrientation.Clockwise or RoofPolygonOrientation.CounterClockwise) ||
            !IsFinite(descriptor.Edge01LengthMm) ||
            !IsFinite(descriptor.Edge12LengthMm) ||
            descriptor.Edge01LengthMm <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm ||
            descriptor.Edge12LengthMm <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm)
        {
            error = RoofDefinitionDataDecodeError.InvalidRigidFootprintDescriptor;
            return false;
        }

        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool Accept(
        RoofDefinitionData candidate,
        out RoofDefinitionData? data,
        out RoofDefinitionDataDecodeError error)
    {
        if (!TryValidate(candidate, out error))
        {
            data = null;
            return false;
        }

        data = candidate;
        error = RoofDefinitionDataDecodeError.None;
        return true;
    }

    private static bool TryReadKind(
        string token,
        out RoofDefinitionDataDecodeError error)
    {
        if (string.Equals(token, SimpleGableToken, StringComparison.Ordinal))
        {
            error = RoofDefinitionDataDecodeError.None;
            return true;
        }

        error = RoofDefinitionDataDecodeError.UnsupportedRoofKind;
        return false;
    }

    private static bool TryReadOrientation(
        string token,
        out RoofPolygonOrientation orientation)
    {
        orientation = token switch
        {
            ClockwiseToken => RoofPolygonOrientation.Clockwise,
            CounterClockwiseToken => RoofPolygonOrientation.CounterClockwise,
            _ => RoofPolygonOrientation.Undefined,
        };
        return orientation != RoofPolygonOrientation.Undefined;
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

        return vertices.All(vertex =>
        {
            var coordinates = vertex.Split(',');
            return coordinates.Length == 2 &&
                   TryParseFinite(coordinates[0], out _) &&
                   TryParseFinite(coordinates[1], out _);
        });
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
